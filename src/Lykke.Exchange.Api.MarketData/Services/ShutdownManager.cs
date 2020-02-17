using System.Threading.Tasks;
using Grpc.Core;
using JetBrains.Annotations;
using Lykke.Sdk;

namespace Lykke.Exchange.Api.MarketData.Services
{
    [UsedImplicitly]
    public class ShutdownManager : IShutdownManager
    {
        private readonly Server _grpcServer;

        public ShutdownManager(Server grpcServer)
        {
            _grpcServer = grpcServer;
        }

        public Task StopAsync()
        {
            return _grpcServer.ShutdownAsync();
        }
    }
}
