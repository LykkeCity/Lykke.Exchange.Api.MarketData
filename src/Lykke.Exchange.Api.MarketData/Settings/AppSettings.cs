using JetBrains.Annotations;
using Lykke.Sdk.Settings;
using Lykke.Service.Assets.Client;

namespace Lykke.Exchange.Api.MarketData.Settings
{
    [UsedImplicitly]
    public class AppSettings : BaseAppSettings
    {
        public MarketDataSettings MarketDataService { get; set; }
        public AssetServiceSettings AssetsServiceClient { get; set; }
    }
}
