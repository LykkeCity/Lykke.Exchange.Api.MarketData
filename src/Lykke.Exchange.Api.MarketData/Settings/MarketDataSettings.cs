using System;
using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Settings
{
    [UsedImplicitly]
    public class MarketDataSettings
    {
        public DbSettings Db { get; set; }
        public RedisSettings Redis { get; set; }
        public RabbitMqSettings RabbitMq { get; set; }
        public CqrsSettings Cqrs { get; set; }
        public int GrpcPort { get; set; }
        public string MarketProfileUrl { get; set; }
        public string CandlesHistoryUrl { get; set; }
        public TimeSpan MarketDataInterval { get; set; }
    }
}
