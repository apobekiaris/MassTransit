namespace OpenAllNight
{
    using System;
    using System.IO;
    using System.Threading;
    using Castle.Core;
    using Castle.Windsor;
    using log4net.Config;
    using MassTransit;
    using MassTransit.Configuration;
    using MassTransit.Services.HealthMonitoring.Configuration;
    using MassTransit.Services.Subscriptions.Configuration;
    using MassTransit.Transports.Msmq;
    using MassTransit.WindsorIntegration;
    using Testers;

    internal class Program
    {
        private static readonly DateTime _startedAt = DateTime.Now;
        private static DateTime lastPrint = DateTime.Now;

        private static void Main()
        {
            /////setup
            XmlConfigurator.ConfigureAndWatch(new FileInfo("log4net.xml"));

            WindsorContainer c = new DefaultMassTransitContainer();
            IEndpointFactory ef = EndpointFactoryConfigurator.New(e => e.RegisterTransport<MsmqEndpoint>());
            c.Kernel.AddComponentInstance("endpointFactory", typeof (IEndpointFactory), ef);

            c.AddComponentLifeStyle("counter", typeof (Counter), LifestyleType.Singleton);
            c.AddComponentLifeStyle("rvaoeuaoe", typeof (CacheUpdateResponseHandler), LifestyleType.Transient);

            IServiceBus bus = ServiceBusConfigurator.New(b =>
            {
                b.ReceiveFrom("msmq://localhost/mt_client");
                b.ConfigureService<SubscriptionClientConfigurator>(sc => sc.SetSubscriptionServiceEndpoint("msmq://localhost/mt_subscriptions"));
                b.ConfigureService<HealthClientConfigurator>(hc=>hc.SetHeartbeatInterval(10));
            });
            c.Kernel.AddComponentInstance("bus", typeof(IServiceBus), bus);
            
            bus.Subscribe<CacheUpdateResponseHandler>();


            IEndpoint ep = c.Resolve<IEndpointFactory>().GetEndpoint(new Uri("msmq://localhost/mt_subscriptions"));
            var subTester = new SubscriptionServiceTester(ep, bus, c.Resolve<Counter>());
            var healthTester = new HealthServiceTester(c.Resolve<Counter>(), bus);
            bus.Subscribe(healthTester);
            ///////


            Console.WriteLine("Please enter the number of hours you would like this test to run for?");
            Console.WriteLine("(use 0.1 for 6 minutes)");
            Console.WriteLine("(use 0.016 for 1 minute)");
            string input = Console.ReadLine();
           double hours = double.Parse(input ?? "0");
            DateTime stopTime = DateTime.Now.AddHours(hours);


            Console.WriteLine("Test Started");
            var rand = new Random();
            while (DateTime.Now < stopTime)
            {
                subTester.Test();
                healthTester.Test();

                Thread.Sleep(rand.Next(5, 10)*1000);
                PrintTime(bus, c.Resolve<Counter>());
            }

            //print final stuff (probably do this by tester)
            subTester.Results();
            Console.WriteLine("Done (press any key to exit)");
            Console.ReadKey(true);
        }

        private static void Initialize()
        {
        }

        private static void PrintTime(IServiceBus bus, Counter counter)
        {
            TimeSpan ts = DateTime.Now.Subtract(lastPrint);
            if (ts.Minutes >= 1)
            {
                Console.WriteLine("Elapsed Time: {0} mins, So far I have - Sent: {1}, Received: {2}, Published: {3}, Received: {4}",
                                  (int) ((DateTime.Now - _startedAt).TotalMinutes), counter.MessagesSent, counter.MessagesReceived, counter.PublishCount, SimpleMessageHandler.MessageCount);

                lastPrint = DateTime.Now;
            }
        }
    }


    public class SimpleMessageHandler :
        Consumes<SimpleMessage>.All
    {
        private static long _messageCount;

        public static long MessageCount
        {
            get { return _messageCount; }
        }

        #region All Members

        public void Consume(SimpleMessage message)
        {
            Interlocked.Increment(ref _messageCount);
        }

        #endregion
    }

    [Serializable]
    public class SimpleMessage
    {
        private readonly string _data = new string('*', 1024);
    }
}