using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using JetBrains.Annotations;
using Lykke.Exchange.Api.MarketData.RabbitMqSubscribers;
using Lykke.Sdk;

namespace Lykke.Exchange.Api.MarketData.Services
{
    [UsedImplicitly]
    public class StartupManager : IStartupManager
    {
        private readonly Server _grpcServer;
        private readonly InitService _initService;
        private readonly QuotesFeedSubscriber _quotesFeedSubscriber;
        private readonly LimitOrdersSubscriber _limitOrdersSubscriber;

        public StartupManager(
            Server grpcServer,
            InitService initService,
            QuotesFeedSubscriber quotesFeedSubscriber,
            LimitOrdersSubscriber limitOrdersSubscriber
            )
        {
            _grpcServer = grpcServer;
            _initService = initService;
            _quotesFeedSubscriber = quotesFeedSubscriber;
            _limitOrdersSubscriber = limitOrdersSubscriber;
        }

        public async Task StartAsync()
        {
            var sw = new Stopwatch();
            Console.WriteLine("Start init...");
            sw.Start();
            await _initService.LoadAsync();
            sw.Stop();
            Console.WriteLine($"Init finished {sw.ElapsedMilliseconds} msec. [{sw.Elapsed}]");
            _quotesFeedSubscriber.Start();
            _limitOrdersSubscriber.Start();
            _grpcServer.Start();
            Console.WriteLine($"Grpc server listening on: {_grpcServer.Ports.First().Host}:{_grpcServer.Ports.First().Port}");
        }
    }
}
