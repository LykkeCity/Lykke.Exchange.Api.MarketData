using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Settings.SlackNotifications
{
    [UsedImplicitly]
    public class SlackNotificationsSettings
    {
        public AzureQueueSettings AzureQueue { get; set; }
    }
}
