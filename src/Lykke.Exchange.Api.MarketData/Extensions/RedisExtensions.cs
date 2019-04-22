using System.Linq;
using System.Reflection;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.Extensions
{
    public static class RedisExtensions
    {
        public static HashEntry[] ToHashEntries(this object obj)
        {
            PropertyInfo[] properties = obj.GetType().GetProperties();
            var skipNames = new[] {"Parser", "Descriptor"};
            
            return properties
                .Where(x => x.GetValue(obj) != null && !skipNames.Contains(x.Name))
                .Select(property => new HashEntry(property.Name, property.GetValue(obj)
                    .ToString())).ToArray();
        }
        
        public static HashEntry[] ToMarketSliceHash(this MarketSlice marketSlice)
        {
            return new[]
            {
                new HashEntry(nameof(marketSlice.AssetPairId), marketSlice.AssetPairId),
                new HashEntry(nameof(marketSlice.VolumeBase), marketSlice.VolumeBase),
                new HashEntry(nameof(marketSlice.VolumeQuote), marketSlice.VolumeQuote),
                new HashEntry(nameof(marketSlice.PriceChange), marketSlice.PriceChange),
                new HashEntry(nameof(marketSlice.LastPrice), marketSlice.LastPrice),
                new HashEntry(nameof(marketSlice.Bid), marketSlice.Bid),
                new HashEntry(nameof(marketSlice.Ask), marketSlice.Ask),
                new HashEntry(nameof(marketSlice.High), marketSlice.High),
                new HashEntry(nameof(marketSlice.Low), marketSlice.Low)
            };
        }
        
        public static MarketSlice ToMarketSlice(this HashEntry[] hashEntry)
        {
            var marketSlice = new MarketSlice();
            
            var hashDict = hashEntry.ToDictionary();

            if (hashDict.TryGetValue(nameof(MarketSlice.AssetPairId), out var assetPair))
                marketSlice.AssetPairId = assetPair;
            
            if (hashDict.TryGetValue(nameof(MarketSlice.VolumeBase), out var volumeBase))
                marketSlice.VolumeBase = volumeBase;
            
            if (hashDict.TryGetValue(nameof(MarketSlice.VolumeQuote), out var volumeQuote))
                marketSlice.VolumeQuote = volumeQuote;
            
            if (hashDict.TryGetValue(nameof(MarketSlice.PriceChange), out var priceChange))
                marketSlice.PriceChange = priceChange;
            
            if (hashDict.TryGetValue(nameof(MarketSlice.LastPrice), out var lastPrice))
                marketSlice.LastPrice = lastPrice;
            
            if (hashDict.TryGetValue(nameof(MarketSlice.Bid), out var bid))
                marketSlice.Bid = bid;

            if (hashDict.TryGetValue(nameof(MarketSlice.Ask), out var ask))
                marketSlice.Ask = ask;
            
            if (hashDict.TryGetValue(nameof(MarketSlice.High), out var high))
                marketSlice.High = high;
            
            if (hashDict.TryGetValue(nameof(MarketSlice.Low), out var low))
                marketSlice.Low = low;

            return marketSlice;
        }
    }
}
