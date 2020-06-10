using System;
using System.Globalization;
using Lykke.Exchange.Api.MarketData.Models;

namespace Lykke.Exchange.Api.MarketData.Extensions
{
    internal static class Extensions
    {
        internal static Ticker ToTicker(this MarketSlice marketSlice)
        {
            return new Ticker(marketSlice.AssetPairId)
            {
                AssetPairId = marketSlice.AssetPairId,
                VolumeBase = string.IsNullOrEmpty(marketSlice.VolumeBase)
                    ? 0
                    : decimal.Parse(marketSlice.VolumeBase, NumberStyles.Any, CultureInfo.InvariantCulture),
                VolumeQuote = string.IsNullOrEmpty(marketSlice.VolumeQuote)
                    ? 0
                    : decimal.Parse(marketSlice.VolumeQuote, NumberStyles.Any, CultureInfo.InvariantCulture),
                PriceChange = string.IsNullOrEmpty(marketSlice.PriceChange)
                    ? 0
                    : decimal.Parse(marketSlice.PriceChange, NumberStyles.Any, CultureInfo.InvariantCulture),
                LastPrice = string.IsNullOrEmpty(marketSlice.LastPrice)
                    ? 0
                    : decimal.Parse(marketSlice.LastPrice, NumberStyles.Any, CultureInfo.InvariantCulture),
                High = string.IsNullOrEmpty(marketSlice.High)
                    ? 0
                    : decimal.Parse(marketSlice.High, NumberStyles.Any, CultureInfo.InvariantCulture),
                Low = string.IsNullOrEmpty(marketSlice.Low)
                    ? 0
                    : decimal.Parse(marketSlice.Low, NumberStyles.Any, CultureInfo.InvariantCulture)
            };
        }

        internal static Price ToPrice(this MarketSlice marketSlice)
        {
            return new Price
            {
                PartitionKey = Price.GetPk(),
                RowKey = marketSlice.AssetPairId,
                UpdatedDt = DateTime.UtcNow,
                AssetPairId = marketSlice.AssetPairId,
                Bid = string.IsNullOrEmpty(marketSlice.Bid)
                    ? 0
                    : decimal.Parse(marketSlice.Bid, NumberStyles.Any, CultureInfo.InvariantCulture),
                Ask = string.IsNullOrEmpty(marketSlice.Ask)
                    ? 0
                    : decimal.Parse(marketSlice.Ask, NumberStyles.Any, CultureInfo.InvariantCulture)
            };
        }
    }
}
