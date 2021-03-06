using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Web.Routing;
using FubuCore;
using FubuMVC.Core;
using FubuMVC.Core.Runtime;
using Gate;
using Gate.Helpers;
using Gate.Kayak;
using Kayak;

namespace FubuMVC.OwinHost
{
    public class FubuOwinHost
    {
        private readonly IApplicationSource _source;
        private readonly ISchedulerDelegate _schedulerDelegate;
        private IPEndPoint _listeningEndpoint;
        private AppDelegate _applicationDelegate;
        private IScheduler _scheduler;
        private IServer _server;
        private int _port;
        private bool _latched;
        private IDisposable _kayakListenerDisposer;

        public FubuOwinHost(IApplicationSource source) : this(source, new SchedulerDelegate())
        {
        }

        public FubuOwinHost(IApplicationSource source, ISchedulerDelegate schedulerDelegate)
        {
            _source = source;
            _schedulerDelegate = schedulerDelegate;
        }

        public bool Verbose { get; set; }

        public void RunApplication(int port, Action<FubuRuntime> activation)
        {
            _port = port;


            _listeningEndpoint = new IPEndPoint(IPAddress.Any, _port);
            Console.WriteLine("Listening on " + _listeningEndpoint);

            _applicationDelegate = AppBuilder.BuildConfiguration(x => x.RescheduleCallbacks().Run(ExecuteRequest));

            _scheduler = KayakScheduler.Factory.Create(_schedulerDelegate);
            _server = KayakServer.Factory.CreateGate(_applicationDelegate, _scheduler, null);

            if (_listeningEndpoint == null)
            {
                throw new InvalidOperationException("Start() can only be called after RunApplication() and Stop()");
            }

            var runtime = rebuildFubuMVCApplication();

            _kayakListenerDisposer = _server.Listen(_listeningEndpoint);
                _scheduler.Post(() => ThreadPool.QueueUserWorkItem(o => activation(runtime)));
                _scheduler.Start();


            
        }

        private FubuRuntime rebuildFubuMVCApplication()
        {
            RouteTable.Routes.Clear();
            return _source.BuildApplication().Bootstrap();
        }

        public void Stop()
        {
            try
            {
                _scheduler.Stop();

            }
            catch (Exception)
            {
                // That's right, shut this puppy down
            }

            _server.SafeDispose();
        }

        public void Recycle(Action<FubuRuntime> activation)
        {
            _latched = true;
            var runtime = rebuildFubuMVCApplication();
            _latched = false;

            activation(runtime);
        }


        public void ExecuteRequest(IDictionary<string, object> env, ResultDelegate result, Action<Exception> fault)
        {
            if (_latched) return;

            var request = new Request(env);
            var response = new Response(result);

            if (Verbose) Console.WriteLine("Received " + request.Path);

            var context = new GateHttpContext(request);
            var routeData = RouteTable.Routes.GetRouteData(context);
           
            if (routeData == null)
            {
                // TODO -- try to do it by mapping the files
                write404(response);
            }
            else
            {
                executeRoute(request, routeData, response, fault);
            }
        
            // TODO -- return 404 if the route is not found

            if (Verbose) Console.WriteLine(" ({0})", response.Status);
        }

        private static void write404(Response response)
        {
            response.Status = "404";
            response.Write("Sorry, I can't find this resource");
        }

        private static void executeRoute(Request request, RouteData routeData, Response response, Action<Exception> fault)
        {
            var arguments = new OwinServiceArguments(routeData, request, response);

            var invoker = routeData.RouteHandler.As<FubuRouteHandler>().Invoker;

            try
            {
                invoker.Invoke(arguments, routeData.Values);
            }
            catch (Exception ex)
            {
                response.Status = "500";
                response.Write("FubuMVC has detected an exception\r\n");
                response.Write(ex.ToString());
            }
            finally
            {
                response.Finish();
            }
        }

    }

}