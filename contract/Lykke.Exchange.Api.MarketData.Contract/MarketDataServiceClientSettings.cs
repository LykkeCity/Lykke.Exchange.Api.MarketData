using Lykke.SettingsReader.Attributes;

namespace Lykke.Exchange.Api.MarketData.Contract
{
    public class MarketDataServiceClientSettings
    {
        [HttpCheck("api/isalive")]
        public string ServiceUrl { get; set; }
        public string GrpcServiceUrl { get; set; }
    }
}
