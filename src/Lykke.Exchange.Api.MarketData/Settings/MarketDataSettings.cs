using System;
using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Settings
{
    [UsedImplicitly]
    public class MarketDataSettings
    {
        public DbSettings Db { get; set; }
        public TimeSpan Period { get; set; }
    }
}
