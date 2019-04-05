using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Settings.SlackNotifications
{
    [UsedImplicitly]
    public class AzureQueueSettings
    {
        public string ConnectionString { get; set; }

        public string QueueName { get; set; }
    }
}
