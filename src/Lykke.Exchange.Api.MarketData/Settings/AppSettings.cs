using JetBrains.Annotations;
using Lykke.Sdk.Settings;

namespace Lykke.Exchange.Api.MarketData.Settings
{
    [UsedImplicitly]
    public class AppSettings : BaseAppSettings
    {
        public MarketDataSettings MarketDataService { get; set; }
    }
}
