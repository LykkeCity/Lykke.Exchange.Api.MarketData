using System.Threading.Tasks;
using Grpc.Core;
using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Services
{
    [UsedImplicitly]
    public class MarketDataServiceClient : MarketDataService.MarketDataServiceBase
    {
        public override Task<MarketDataResponse> GetMarketData(MarketDataRequest request, ServerCallContext context)
        {
            return Task.FromResult(new MarketDataResponse{AssetPairId = request.AssetPairId});
        }
    }
}
