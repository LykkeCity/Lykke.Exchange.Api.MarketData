using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Services
{
    [UsedImplicitly]
    public class MarketDataServiceClient : MarketDataService.MarketDataServiceBase
    {
        private readonly RedisService _redisService;

        public MarketDataServiceClient(RedisService redisService)
        {
            _redisService = redisService;
        }

        public override Task<MarketSlice> GetAssetPairMarketData(MarketDataRequest request, ServerCallContext context)
        {
            return _redisService.GetMarketDataAsync(request.AssetPairId);
        }

        public override async Task<MarketDataResponse> GetMarketData(Empty request, ServerCallContext context)
        {
            var response = new MarketDataResponse();

            var data = await _redisService.GetMarketDataAsync();
            
            response.Items.AddRange(data);

            return response;
        }
    }
}
