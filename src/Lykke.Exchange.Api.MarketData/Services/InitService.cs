using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Lykke.Exchange.Api.MarketData.Extensions;
using Lykke.Service.CandlesHistory.Client;
using Lykke.Service.CandlesHistory.Client.Models;
using Lykke.Service.MarketProfile.Client;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.Services
{
    public class InitService
    {
        private readonly IDatabase _database;
        private readonly ILykkeMarketProfile _marketProfileClient;
        private readonly ICandleshistoryservice _candlesHistoryClient;

        public InitService(
            IDatabase database,
            ILykkeMarketProfile marketProfileClient,
            ICandleshistoryservice candlesHistoryClient
        )
        {
            _database = database;
            _marketProfileClient = marketProfileClient;
            _candlesHistoryClient = candlesHistoryClient;
        }

        public async Task LoadAsync()
        {
            var now = DateTime.UtcNow;

            var assetPairsTask = _candlesHistoryClient.GetAvailableAssetPairsAsync();
            var marketDataTask = GetMarketProfilesAsync();

            await Task.WhenAll(assetPairsTask, marketDataTask);

            var todayCandlesTask = GetCandlesAsync(now - TimeSpan.FromHours(24), now.AddMinutes(5),
                assetPairsTask.Result, CandlePriceType.Trades, CandleTimeInterval.Min5);
            var lastMonthCandlesTask = GetCandlesAsync(now.AddYears(-1) - TimeSpan.FromHours(24), now.AddMonths(1),
                assetPairsTask.Result, CandlePriceType.Trades, CandleTimeInterval.Month);

            await Task.WhenAll(todayCandlesTask, lastMonthCandlesTask);

            var marketData = marketDataTask.Result;
            var todayCandles = todayCandlesTask.Result;
            var lastMonthCandles = lastMonthCandlesTask.Result;

            foreach (var todayCandle in todayCandles)
            {
                UpdateCandlesInfo(todayCandle.Key, todayCandle.Value, marketData);
            }

            foreach (var lastMonthCandle in lastMonthCandles)
            {
                UpdateLastPrice(lastMonthCandle.Key, lastMonthCandle.Value, marketData);
            }

            await SaveMarketDataAsync(marketData, todayCandles);
        }

        private async Task SaveMarketDataAsync(List<MarketSlice> marketData, Dictionary<string, IList<Candle>> prices)
        {
            var sw = new Stopwatch();
            Console.WriteLine("Saving data to redis...");
            sw.Start();
            var nowDate = DateTime.UtcNow;
            var now = nowDate.ToUnixTime();
            var tasks = new List<Task>();

            var pricesValue = new Dictionary<string, SortedSetEntry[]>();

            foreach (var price in prices)
            {
                pricesValue.Add(price.Key,
                    price.Value.Select(x =>
                        new SortedSetEntry(RedisExtensions.SerializeWithTimestamp((decimal) x.Open, x.DateTime),
                            x.DateTime.ToUnixTime())).ToArray());
            }

            foreach (MarketSlice marketSlice in marketData)
            {
                tasks.Add(_database.HashSetAsync(RedisService.GetMarketDataKey(marketSlice.AssetPairId),
                    marketSlice.ToMarketSliceHash()));
                tasks.Add(_database.SortedSetAddAsync(RedisService.GetAssetPairsKey(), marketSlice.AssetPairId, 0));

                string baseVolumeKey = RedisService.GetMarketDataBaseVolumeKey(marketSlice.AssetPairId);
                string quoteVolumeKey = RedisService.GetMarketDataQuoteVolumeKey(marketSlice.AssetPairId);

                tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(baseVolumeKey, 0, now - 1, Exclude.None,
                    CommandFlags.FireAndForget));
                tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(quoteVolumeKey, 0, now - 1, Exclude.None,
                    CommandFlags.FireAndForget));

                if (!string.IsNullOrEmpty(marketSlice.VolumeBase))
                    tasks.Add(_database.SortedSetAddAsync(baseVolumeKey,
                        RedisExtensions.SerializeWithTimestamp(
                            decimal.Parse(marketSlice.VolumeBase, CultureInfo.InvariantCulture), nowDate), now));

                if (!string.IsNullOrEmpty(marketSlice.VolumeQuote))
                    tasks.Add(_database.SortedSetAddAsync(quoteVolumeKey,
                        RedisExtensions.SerializeWithTimestamp(
                            decimal.Parse(marketSlice.VolumeQuote, CultureInfo.InvariantCulture), nowDate), now));

                await Task.WhenAll(tasks);
                tasks.Clear();
            }

            tasks = new List<Task>();

            foreach (var entry in pricesValue)
            {
                tasks.Add(_database.SortedSetAddAsync(RedisService.GetMarketDataPriceKey(entry.Key), entry.Value));
            }

            await Task.WhenAll(tasks);
            sw.Stop();

            Console.WriteLine($"Saved data to redis [{sw.Elapsed}]");
        }

        private void UpdateCandlesInfo(string assetPairId, IList<Candle> candles, List<MarketSlice> marketData)
        {
            var firstCandle = candles.First();
            var lastCandle = candles.Last();

            var marketSlice = new MarketSlice
            {
                AssetPairId = assetPairId,
                VolumeBase = candles.Sum(c => c.TradingVolume).ToString(CultureInfo.InvariantCulture),
                VolumeQuote = candles.Sum(c => c.TradingOppositeVolume).ToString(CultureInfo.InvariantCulture),
                PriceChange = firstCandle.Open > 0
                    ? ((lastCandle.Close - firstCandle.Open) / firstCandle.Open).ToString(CultureInfo.InvariantCulture)
                    : "0",
                High = candles.Max(c => c.High).ToString(CultureInfo.InvariantCulture),
                Low = candles.Min(c => c.Low).ToString(CultureInfo.InvariantCulture),
            };

            var existingRecord = marketData.FirstOrDefault(x => x.AssetPairId == assetPairId);

            if (existingRecord != null)
            {
                existingRecord.VolumeBase = marketSlice.VolumeBase;
                existingRecord.VolumeQuote = marketSlice.VolumeQuote;
                existingRecord.PriceChange = marketSlice.PriceChange;
                existingRecord.High = marketSlice.High;
                existingRecord.Low = marketSlice.Low;
            }
            else
            {
                marketData.Add(marketSlice);
            }
        }

        private void UpdateLastPrice(string assetPairId, IList<Candle> candles, List<MarketSlice> marketData)
        {
            var lastCandle = candles.LastOrDefault();
            var existingRecord = marketData.FirstOrDefault(x => x.AssetPairId == assetPairId);

            if (lastCandle != null && existingRecord != null)
            {
                existingRecord.LastPrice = lastCandle.Close.ToString(CultureInfo.InvariantCulture);
            }
        }

        private async Task<List<MarketSlice>> GetMarketProfilesAsync()
        {
            var marketData = new List<MarketSlice>();

            var marketProfiles = await _marketProfileClient.ApiMarketProfileGetAsync();

            marketData.AddRange(marketProfiles
                .Select(x => new MarketSlice
                {
                    AssetPairId = x.AssetPair,
                    Ask = x.AskPrice.ToString(CultureInfo.InvariantCulture),
                    Bid = x.BidPrice.ToString(CultureInfo.InvariantCulture)
                }));

            return marketData;
        }

        private async Task<Dictionary<string, IList<Candle>>> GetCandlesAsync(DateTime from, DateTime to,
            IList<string> assetPairs, CandlePriceType priceType, CandleTimeInterval interval)
        {
            var todayCandles = new Dictionary<string, IList<Candle>>();

            var todayCandleHistoryForPairs = await _candlesHistoryClient.GetCandlesHistoryBatchAsync(assetPairs,
                priceType, interval, from, to);

            if (todayCandleHistoryForPairs == null) // Some technical issue has happened without an exception.
                throw new InvalidOperationException("Could not obtain today's Min5 trade candles at all.");

            if (!todayCandleHistoryForPairs.Any())
                return todayCandles;

            foreach (var historyForPair in todayCandleHistoryForPairs)
            {
                if (historyForPair.Value?.History == null ||
                    !historyForPair.Value.History.Any())
                    continue;

                todayCandles.Add(historyForPair.Key, historyForPair.Value.History);
            }

            return todayCandles;
        }
    }
}
