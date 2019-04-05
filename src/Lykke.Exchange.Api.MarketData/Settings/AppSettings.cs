using JetBrains.Annotations;
using Lykke.Exchange.Api.MarketData.Settings.SlackNotifications;

namespace Lykke.Exchange.Api.MarketData.Settings
{
    [UsedImplicitly]
    public class AppSettings
    {
        public MarketDataSettings MarketDataService { get; set; }
        public SlackNotificationsSettings SlackNotifications { get; set; }
        public MonitoringServiceClientSettings MonitoringServiceClient { get; set; }
    }
}
