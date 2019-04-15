using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Settings
{
    [UsedImplicitly]
    public class MarketDataSettings
    {
        public DbSettings Db { get; set; }
        public RedisSettings Redis { get; set; }
        public string MarketProfileUrl { get; set; }
        public string CandlesHistoryUrl { get; set; }
        public string TradesAdapterUrl { get; set; }
    }
}
