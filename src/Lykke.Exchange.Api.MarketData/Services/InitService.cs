using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Lykke.Exchange.Api.MarketData.Extensions;
using Lykke.Service.CandlesHistory.Client;
using Lykke.Service.CandlesHistory.Client.Models;
using Lykke.Service.MarketProfile.Client;
using Lykke.Service.TradesAdapter.Client;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.Services
{
    public class InitService
    {
        private readonly IDatabase _database;
        private readonly ILykkeMarketProfile _marketProfileClient;
        private readonly ICandleshistoryservice _candlesHistoryClient;
        private readonly ITradesAdapterClient _tradesAdapterClient;

        public InitService(
            IDatabase database,
            ILykkeMarketProfile marketProfileClient,
            ICandleshistoryservice candlesHistoryClient,
            ITradesAdapterClient tradesAdapterClient
            )
        {
            _database = database;
            _marketProfileClient = marketProfileClient;
            _candlesHistoryClient = candlesHistoryClient;
            _tradesAdapterClient = tradesAdapterClient;
        }

        public async Task LoadAsync()
        {
            var marketDataTask = GetMarketProfilesAsync();
            var todayCandlesTask = GetCandlesAsync();

            await Task.WhenAll(marketDataTask, todayCandlesTask);

            var marketData = marketDataTask.Result;
            var todayCandles = todayCandlesTask.Result;

            foreach (var todayCandle in todayCandles)
            {
                UpdateCandlesInfo(todayCandle.Key, todayCandle.Value, marketData);
            }

            await Task.WhenAll(
                UpdateLastPricesAsync(marketData),
                SaveMarketDataAsync(marketData, todayCandles)
            );
        }

        private async Task SaveMarketDataAsync(List<MarketSlice> marketData, Dictionary<string,IList<Candle>> prices)
        {
            var nowDate = DateTime.UtcNow;
            var now = nowDate.ToUnixTime();
            var tasks = new List<Task>();

            var pricesValue = new Dictionary<string, SortedSetEntry[]>();

            foreach (var price in prices)
            {
                pricesValue.Add(price.Key, price.Value.Select(x => new SortedSetEntry(RedisExtensions.SerializeWithTimestamp((decimal)x.Open, x.DateTime), x.DateTime.ToUnixTime())).ToArray());
            }

            foreach (MarketSlice marketSlice in marketData)
            {
                tasks.Add(_database.HashSetAsync(RedisService.GetMarketDataKey(marketSlice.AssetPairId), marketSlice.ToMarketSliceHash()));
                tasks.Add(_database.SortedSetAddAsync(RedisService.GetAssetPairsKey(), marketSlice.AssetPairId, 0));

                string baseVolumeKey = RedisService.GetMarketDataBaseVolumeKey(marketSlice.AssetPairId);
                string quoteVolumeKey = RedisService.GetMarketDataQuoteVolumeKey(marketSlice.AssetPairId);

                tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(baseVolumeKey, 0, now - 1, Exclude.None, CommandFlags.FireAndForget));
                tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(quoteVolumeKey, 0, now - 1, Exclude.None, CommandFlags.FireAndForget));

                if (!string.IsNullOrEmpty(marketSlice.VolumeBase))
                    tasks.Add(_database.SortedSetAddAsync(baseVolumeKey, RedisExtensions.SerializeWithTimestamp(decimal.Parse(marketSlice.VolumeBase, CultureInfo.InvariantCulture), nowDate), now));

                if (!string.IsNullOrEmpty(marketSlice.VolumeQuote))
                    tasks.Add(_database.SortedSetAddAsync(quoteVolumeKey, RedisExtensions.SerializeWithTimestamp(decimal.Parse(marketSlice.VolumeQuote, CultureInfo.InvariantCulture), nowDate), now));

                await Task.WhenAll(tasks);
                tasks.Clear();
            }

            tasks = new List<Task>();

            foreach (var entry in pricesValue)
            {
                tasks.Add(_database.SortedSetAddAsync(RedisService.GetMarketDataPriceKey(entry.Key), entry.Value));
            }

            await Task.WhenAll(tasks);
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

        private async Task UpdateLastPricesAsync(List<MarketSlice> marketData)
        {
            foreach (var marketSlice in marketData)
            {
                // it's OK to call it multiple times on init for now
                double? lastPrice = await GetLastPriceAsync(marketSlice.AssetPairId);

                if (lastPrice != null)
                    marketSlice.LastPrice = lastPrice.Value.ToString(CultureInfo.InvariantCulture);
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

        private async Task<Dictionary<string, IList<Candle>>> GetCandlesAsync()
        {
            var todayCandles = new Dictionary<string, IList<Candle>>();

            var now = DateTime.UtcNow;
            // inclusive
            var from = now - TimeSpan.FromHours(24);
            // exclusive
            var to = now.AddMinutes(5);

            var assetPairs = await _candlesHistoryClient.GetAvailableAssetPairsAsync();
            var todayCandleHistoryForPairs = await _candlesHistoryClient.GetCandlesHistoryBatchAsync(assetPairs,
                CandlePriceType.Trades, CandleTimeInterval.Min5, from, to);

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

        private async Task<double?> GetLastPriceAsync(string assetPairId)
        {
            var tradesResponse = await _tradesAdapterClient.GetTradesByAssetPairIdAsync(assetPairId, 0, 1);

            if (tradesResponse?.Records != null && tradesResponse.Records.Any())
            {
                return tradesResponse.Records.First().Price;
            }

            return null;
        }
    }
}
