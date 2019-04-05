using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using JetBrains.Annotations;
using Lykke.Sdk;

namespace Lykke.Exchange.Api.MarketData.Services
{
    [UsedImplicitly]
    public class StartupManager : IStartupManager
    {
        private readonly Server _grpcServer;

        public StartupManager(Server grpcServer)
        {
            _grpcServer = grpcServer;
        }
        
        public Task StartAsync()
        {
            _grpcServer.Start();
            Console.WriteLine($"Grpc server listening on: {_grpcServer.Ports.First().Host}:{_grpcServer.Ports.First().Port}");
            return Task.CompletedTask;
        }
    }
}
