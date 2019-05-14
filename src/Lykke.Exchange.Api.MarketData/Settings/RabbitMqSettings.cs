using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Settings
{
    [UsedImplicitly]
    public class RabbitMqSettings
    {
        public string QuotesConnectionString { get; set; }
        public string LimitOrdersConnectionString { get; set; }
        public string QuotesExchangeName { get; set; }
        public string LimitOrdersExchangeName { get; set; }
    }
}
