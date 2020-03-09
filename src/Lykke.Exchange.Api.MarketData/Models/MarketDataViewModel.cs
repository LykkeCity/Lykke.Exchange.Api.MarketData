using System;
using System.Collections.Generic;

namespace Lykke.Exchange.Api.MarketData.Models
{
    public class MarketDataViewModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public MarketSlice Slice { get; set; }
        public List<(decimal data, string dateTime)> BaseVolumes { get; set; }
        public List<(decimal data, string dateTime)> QuoteVolumes { get; set; }
        public List<(decimal data, string dateTime)> PriceValues { get; set; }
    }
}
