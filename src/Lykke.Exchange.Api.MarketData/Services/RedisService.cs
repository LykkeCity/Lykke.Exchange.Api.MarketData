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
        public static string GetMarketDataHighKey(string assetPairId) => $"MarketData:data:{assetPairId}:High";
        public static string GetMarketDataLowKey(string assetPairId) => $"MarketData:data:{assetPairId}:Low";
        public static string GetMarketDataOpenPriceKey(string assetPairId) => $"MarketData:data:{assetPairId}:OpenPrice";

        public async Task<MarketSlice> GetMarketDataAsync(string assetPair)
        {
            var data = await _database.HashGetAllAsync(GetMarketDataKey(assetPair));
            var marketSlice = data?.ToMarketSlice() ?? new MarketSlice {AssetPairId = assetPair};

            var nowDate = DateTime.UtcNow;
            var now = nowDate.ToUnixTime();
            var from = (nowDate - _marketDataInterval).ToUnixTime();

            var baseVolumesDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataBaseVolumeKey(assetPair), from, now);
            var quoteVolumesDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataQuoteVolumeKey(assetPair), from, now);
            var openPriceDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataOpenPriceKey(assetPair), from, now);
            var highDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataHighKey(assetPair), from, now);
            var lowDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataLowKey(assetPair), from, now);

            await Task.WhenAll(baseVolumesDataTask, quoteVolumesDataTask, openPriceDataTask, highDataTask, lowDataTask);

            decimal baseVolume = baseVolumesDataTask.Result
                .Where(x => x.HasValue)
                .Sum(x => RedisExtensions.DeserializeTimestamped<decimal>(x));

            decimal quoteVolume = quoteVolumesDataTask.Result
                .Where(x => x.HasValue)
                .Sum(x => RedisExtensions.DeserializeTimestamped<decimal>(x));

            decimal high = highDataTask.Result.Any(x => x.HasValue) ? highDataTask.Result
                .Where(x => x.HasValue)
                .Max(x => RedisExtensions.DeserializeTimestamped<decimal>(x)) : 0;

            decimal low = lowDataTask.Result.Any(x => x.HasValue) ? lowDataTask.Result
                .Where(x => x.HasValue)
                .Min(x => RedisExtensions.DeserializeTimestamped<decimal>(x)) : 0;

            if (openPriceDataTask.Result.Any() && openPriceDataTask.Result[0].HasValue)
            {
                decimal price = RedisExtensions.DeserializeTimestamped<decimal>(openPriceDataTask.Result[0]);

                if (price > 0)
                {
                    decimal priceChange = (decimal.Parse(marketSlice.LastPrice, CultureInfo.InvariantCulture) - price) / price;
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

        private async Task<List<string>> GetAssetPairsAsync()
        {
            var assetPairs = await _database.SortedSetRangeByScoreAsync(GetAssetPairsKey(), 0, 0);
            return assetPairs.Select(assetPair => (string) assetPair).ToList();
        }
    }
}
