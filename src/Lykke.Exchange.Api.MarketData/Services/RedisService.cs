using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public RedisService(
            IDatabase database
            )
        {
            _database = database;
        }

        public static string GetMarketDataKey(string assetPairId) => $"MarketData:Slice:{assetPairId}";
        public static string GetAssetPairsKey() => "MarketData:AssetPairs";
        public static string GetMarketDataBaseVolumeKey(string assetPairId) => $"MarketData:BaseVolume:{assetPairId}";
        public static string GetMarketDataQuoteVolumeKey(string assetPairId) => $"MarketData:QuoteVolume:{assetPairId}";
        public static string GetMarketDataPriceKey(string assetPairId) => $"MarketData:Price:{assetPairId}";

        public async Task<MarketSlice> GetMarketDataAsync(string assetPair)
        {
            var data = await _database.HashGetAllAsync(GetMarketDataKey(assetPair));
            var marketSlice = data?.ToMarketSlice() ?? new MarketSlice {AssetPairId = assetPair};

            var nowDate = DateTime.UtcNow;
            var now = nowDate.ToUnixTime();
            var from = (nowDate - TimeSpan.FromHours(24)).ToUnixTime();

            decimal baseVolume = 0;
            decimal quoteVolume = 0;

            var baseVolumesDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataBaseVolumeKey(assetPair), from, now);
            var quoteVolumesDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataQuoteVolumeKey(assetPair), from, now);
            var priceDataTask = _database.SortedSetRangeByScoreAsync(GetMarketDataPriceKey(assetPair), from, now);

            await Task.WhenAll(baseVolumesDataTask, quoteVolumesDataTask, priceDataTask);

            foreach (var baseVolumeData in baseVolumesDataTask.Result)
            {
                if (!baseVolumeData.HasValue)
                    continue;

                decimal baseVol = RedisExtensions.DeserializeTimestamped<decimal>(baseVolumeData);
                baseVolume += baseVol;
            }

            foreach (var quoteVolumeData in quoteVolumesDataTask.Result)
            {
                if (!quoteVolumeData.HasValue)
                    continue;

                decimal quoteVol = RedisExtensions.DeserializeTimestamped<decimal>(quoteVolumeData);
                quoteVolume += quoteVol;
            }

            if (priceDataTask.Result.Any() && priceDataTask.Result[0].HasValue)
            {
                decimal price = RedisExtensions.DeserializeTimestamped<decimal>(priceDataTask.Result[0]);

                if (price > 0)
                {
                    decimal priceChange = (decimal.Parse(marketSlice.LastPrice, CultureInfo.InvariantCulture) - price) / price;
                    marketSlice.PriceChange = priceChange.ToString(CultureInfo.InvariantCulture);
                }
            }

            marketSlice.VolumeBase = baseVolume.ToString(CultureInfo.InvariantCulture);
            marketSlice.VolumeQuote = quoteVolume.ToString(CultureInfo.InvariantCulture);

            return marketSlice;
        }

        public async Task<List<MarketSlice>> GetMarketDataAsync()
        {
            var result = new List<MarketSlice>();
            var sw = new Stopwatch();
            sw.Start();
            List<string> assetPairs = await GetAssetPairsAsync();
            sw.Stop();

            Console.WriteLine($"get all asset pairs = {sw.ElapsedMilliseconds} msec");
            sw.Restart();

            var tasks = new List<Task<MarketSlice>>();

            foreach (string assetPair in assetPairs)
            {
                tasks.Add(GetMarketDataAsync(assetPair));
            }

            await Task.WhenAll(tasks);

            result.AddRange(tasks.Select(x => x.Result));

            sw.Stop();
            Console.WriteLine($"get market slices: {sw.ElapsedMilliseconds} msec. [{sw.Elapsed}]");

            return result;
        }

        private async Task<List<string>> GetAssetPairsAsync()
        {
            var assetPairs = await _database.SortedSetRangeByScoreAsync(GetAssetPairsKey(), 0, 0);
            return assetPairs.Select(assetPair => (string) assetPair).ToList();
        }
    }
}
