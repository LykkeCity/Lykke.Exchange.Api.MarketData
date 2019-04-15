using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Services
{
    [UsedImplicitly]
    public class MarketDataServiceClient : MarketDataService.MarketDataServiceBase
    {
        public override Task<MarketSlice> GetAssetPairMarketData(MarketDataRequest request, ServerCallContext context)
        {
            return Task.FromResult(new MarketSlice{AssetPairId = request.AssetPairId});
        }

        public override Task<MarketDataResponse> GetMarketData(Empty request, ServerCallContext context)
        {
            var response = new MarketDataResponse();
            
            response.Items.Add(new List<MarketSlice>());

            return Task.FromResult(response);
        }
    }
}
