using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Lykke.Exchange.Api.MarketData.Extensions;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.Services
{
    public class RedisService
    {
        private readonly IDatabase _database;
        private readonly TimeSpan _marketDataInterval;

        public RedisService(
            IDatabase database,
            TimeSpan marketDataInterval
            )
        {
            _database = database;
            _marketDataInterval = marketDataInterval;
        }

        public static string GetMarketDataKey(string assetPairId) => $"MarketData:data:{assetPairId}:Slice";
        public static string GetAssetPairsKey() => "MarketData:AssetPairs";
        public static string GetMarketDataBaseVolumeKey(string assetPairId) => $"MarketData:data:{assetPairId}:BaseVolume";
        public static string GetMarketDataQuoteVolumeKey(string assetPairId) => $"MarketData:data:{assetPairId}:QuoteVolume";
        public static string GetMarketDataPriceKey(string assetPairId) => $"MarketData:data:{assetPairId}:Price";

        public async Task<MarketSlice> GetMarketDataAsync(string assetPair)
        {
            var data = await _database.HashGetAllAsync(GetMarketDataKey(assetPair));
            var marketSlice = data?.ToMarketSlice() ?? new MarketSlice {AssetPairId = assetPair};

            var nowDate = DateTime.UtcNow;
            var now = nowDate.ToUnixTime();
            var from = (nowDate - _marketDataInterval).ToUnixTime();

            var baseVolumesDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataBaseVolumeKey(assetPair), from, now);
            var quoteVolumesDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataQuoteVolumeKey(assetPair), from, now);
            var pricesDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataPriceKey(assetPair), from, now);

            await Task.WhenAll(baseVolumesDataTask, quoteVolumesDataTask, pricesDataTask);

            RedisValue[] prices = pricesDataTask.Result;

            decimal baseVolume = baseVolumesDataTask.Result
                .Where(x => x.HasValue)
                .Sum(x => RedisExtensions.DeserializeTimestamped<decimal>(x));

            decimal quoteVolume = quoteVolumesDataTask.Result
                .Where(x => x.HasValue)
                .Sum(x => RedisExtensions.DeserializeTimestamped<decimal>(x));

            decimal high = prices.Any(x => x.HasValue) ? prices
                .Where(x => x.HasValue)
                .Max(x => RedisExtensions.DeserializeTimestamped<decimal>(x)) : 0;

            decimal low = prices.Any(x => x.HasValue) ? prices
                .Where(x => x.HasValue)
                .Min(x => RedisExtensions.DeserializeTimestamped<decimal>(x)) : 0;

            if (prices.Any() && prices[0].HasValue)
            {
                decimal price = RedisExtensions.DeserializeTimestamped<decimal>(pricesDataTask.Result[0]);

                if (price > 0)
                {
                    decimal priceChange = (decimal.Parse(marketSlice.LastPrice, NumberStyles.Any, CultureInfo.InvariantCulture) - price) / price;
                    marketSlice.PriceChange = priceChange.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (high > 0)
                marketSlice.High = high.ToString(CultureInfo.InvariantCulture);

            if (low > 0)
                marketSlice.Low = low.ToString(CultureInfo.InvariantCulture);

            marketSlice.VolumeBase = baseVolume.ToString(CultureInfo.InvariantCulture);
            marketSlice.VolumeQuote = quoteVolume.ToString(CultureInfo.InvariantCulture);

            return marketSlice;
        }

        public async Task<List<MarketSlice>> GetMarketDataAsync()
        {
            var result = new List<MarketSlice>();
            List<string> assetPairs = await GetAssetPairsAsync();

            var tasks = new List<Task<MarketSlice>>();

            foreach (string assetPair in assetPairs)
            {
                tasks.Add(GetMarketDataAsync(assetPair));
            }

            await Task.WhenAll(tasks);

            result.AddRange(tasks.Select(x => x.Result));

            return result;
        }

        public TimeSpan GetMarketDataInterval()
        {
            return _marketDataInterval;
        }

        private async Task<List<string>> GetAssetPairsAsync()
        {
            var assetPairs = await _database.SortedSetRangeByScoreAsync(GetAssetPairsKey(), 0, 0);
            return assetPairs.Select(assetPair => (string) assetPair).ToList();
        }
    }
}
