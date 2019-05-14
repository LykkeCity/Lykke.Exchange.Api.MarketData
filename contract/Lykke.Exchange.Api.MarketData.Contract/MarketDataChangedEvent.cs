namespace Lykke.Exchange.Api.MarketData.Contract
{
    public class MarketDataChangedEvent
    {
        public string AssetPairId { get; set; }
        public decimal VolumeBase { get; set; }
        public decimal VolumeQuote { get; set; }
        public decimal PriceChange { get; set; }
        public decimal LastPrice { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
    }
}
