using System.Threading.Tasks;
using Grpc.Core;

namespace Lykke.Exchange.Api.MarketData.Services
{
    public class MarketDataServiceClient : MarketDataService.MarketDataServiceBase
    {
        public override Task<MarketDataResponse> GetMarketData(MarketDataRequest request, ServerCallContext context)
        {
            return Task.FromResult(new MarketDataResponse{AssetPairId = request.AssetPairId});
        }
    }
}
