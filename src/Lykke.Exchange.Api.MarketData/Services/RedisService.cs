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
                if (baseVolumeData.HasValue && decimal.TryParse(baseVolumeData, out var baseVol))
                {
                    baseVolume += baseVol;
                }
            }
            
            foreach (var quoteVolumeData in quoteVolumesDataTask.Result)
            {
                if (quoteVolumeData.HasValue && decimal.TryParse(quoteVolumeData, out var quoteVol))
                {
                    quoteVolume += quoteVol;
                }
            }

            if (priceDataTask.Result.Any() && decimal.TryParse(priceDataTask.Result[0], out decimal price))
            {
                decimal priceChange = (decimal.Parse(marketSlice.LastPrice, CultureInfo.InvariantCulture) - price) / price;
                marketSlice.PriceChange = priceChange.ToString(CultureInfo.InvariantCulture);
            }

            marketSlice.VolumeBase = baseVolume.ToString(CultureInfo.InvariantCulture);
            marketSlice.VolumeQuote = quoteVolume.ToString(CultureInfo.InvariantCulture);

            return marketSlice;
        }

        public async Task<List<MarketSlice>> GetMarketDataAsync()
        {
            var result = new List<MarketSlice>();
            List<string> assetPairs = await GetAssetPairs();

            foreach (string assetPair in assetPairs)
            {
                var marketSlice = await GetMarketDataAsync(assetPair);
                result.Add(marketSlice);
            }

            return result;
        }
        
        private async Task<List<string>> GetAssetPairs()
        {
            var assetPairs = await _database.SortedSetRangeByScoreAsync(GetAssetPairsKey(), 0, 0);
            return assetPairs.Select(assetPair => (string) assetPair).ToList();
        }
    }
}
