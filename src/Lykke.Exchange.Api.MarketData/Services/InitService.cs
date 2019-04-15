using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Lykke.Service.CandlesHistory.Client;
using Lykke.Service.CandlesHistory.Client.Models;
using Lykke.Service.MarketProfile.Client;
using Lykke.Service.MarketProfile.Client.Models;
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
            var marketData = await GetMarketSnapshotAsync();

            // TODO: save in redis
            foreach (MarketSlice marketSlice in marketData)
            {
            }
        }
        
        private async Task<List<MarketSlice>> GetMarketSnapshotAsync(string assetPairId = null)
        {
            var marketData = await GetMarketProfilesAsync(assetPairId);
            var todayCandles = await GetTodaySpotCandlesAsync(assetPairId);
            var lastMonthCandles = await GetLastSpotCandlesAsync(assetPairId);

            foreach (var todayCandle in todayCandles)
            {
                var existingRecord = marketData.FirstOrDefault(x => x.AssetPairId == todayCandle.AssetPairId);
                
                if (existingRecord != null)
                {
                    existingRecord.VolumeBase = todayCandle.VolumeBase;
                    existingRecord.VolumeQuote = todayCandle.VolumeQuote;
                    existingRecord.PriceChange = todayCandle.PriceChange;
                    existingRecord.High = todayCandle.High;
                    existingRecord.Low = todayCandle.Low;
                }
                else
                {
                    marketData.Add(todayCandle);
                }
            }

            // LastPrice
            foreach (var monthCandle in lastMonthCandles)
            {
                var existingRecord = marketData.FirstOrDefault(x => x.AssetPairId == monthCandle.AssetPairId);

                if (existingRecord != null)
                    existingRecord.LastPrice = monthCandle.LastPrice;
                else
                    marketData.Add(monthCandle);
            }

            return marketData;
        }
        
        private async Task<List<MarketSlice>> GetMarketProfilesAsync(string assetPairId = null)
        {
            var marketData = new List<MarketSlice>();

            if (!string.IsNullOrWhiteSpace(assetPairId))
            {
                var result = await _marketProfileClient.ApiMarketProfileByPairCodeGetAsync(assetPairId);
                var marketProfile = result is AssetPairModel m ? m : null;
                marketData.Add(new MarketSlice
                {
                    AssetPairId = assetPairId,
                    Ask = marketProfile?.AskPrice.ToString(CultureInfo.InvariantCulture),
                    Bid = marketProfile?.BidPrice.ToString(CultureInfo.InvariantCulture)
                });
            }
            else
            {
                var marketProfiles = await _marketProfileClient.ApiMarketProfileGetAsync();

                marketData.AddRange(marketProfiles.Select(x => new MarketSlice
                {
                    AssetPairId = x.AssetPair,
                    Ask = x?.AskPrice.ToString(CultureInfo.InvariantCulture),
                    Bid = x?.BidPrice.ToString(CultureInfo.InvariantCulture)
                }));
            }

            return marketData;
        }
        
        private async Task<List<MarketSlice>> GetTodaySpotCandlesAsync(string assetPairId = null)
        {
            var todayCandles = new List<MarketSlice>();

            var now = DateTime.UtcNow;
            // inclusive
            var from = now - TimeSpan.FromHours(24);
            // exclusive
            var to = now.AddMinutes(5); 

            if (!string.IsNullOrWhiteSpace(assetPairId))
            {
                var todayCandleHistory = await _candlesHistoryClient.TryGetCandlesHistoryAsync(assetPairId,
                    CandlePriceType.Trades, CandleTimeInterval.Min5, from, to);

                if (todayCandleHistory?.History == null ||
                    !todayCandleHistory.History.Any())
                    return todayCandles;

                var firstCandle = todayCandleHistory.History.First();
                var lastCandle = todayCandleHistory.History.Last();

                var marketSlice = new MarketSlice
                {
                    AssetPairId = assetPairId,
                    VolumeBase = todayCandleHistory.History.Sum(c => c.TradingVolume).ToString(CultureInfo.InvariantCulture),
                    VolumeQuote = todayCandleHistory.History.Sum(c => c.TradingOppositeVolume).ToString(CultureInfo.InvariantCulture),
                    PriceChange = firstCandle.Open > 0
                        ? ((lastCandle.Close - firstCandle.Open) / firstCandle.Open).ToString(CultureInfo.InvariantCulture)
                        : "0",
                    High = todayCandleHistory.History.Max(c => c.High).ToString(CultureInfo.InvariantCulture),
                    Low = todayCandleHistory.History.Min(c => c.Low).ToString(CultureInfo.InvariantCulture),
                };

                todayCandles.Add(marketSlice);
            }
            else
            {
                var assetPairs = await _candlesHistoryClient.GetAvailableAssetPairsAsync();
                var todayCandleHistoryForPairs = await _candlesHistoryClient.GetCandlesHistoryBatchAsync(assetPairs,
                    CandlePriceType.Trades, CandleTimeInterval.Min5, from, to);

                if (todayCandleHistoryForPairs == null) // Some technical issue has happened without an exception.
                    throw new InvalidOperationException("Could not obtain today's Hour Spot trade candles at all.");

                if (!todayCandleHistoryForPairs.Any())
                    return todayCandles;

                foreach (var historyForPair in todayCandleHistoryForPairs)
                {
                    if (historyForPair.Value?.History == null ||
                        !historyForPair.Value.History.Any())
                        continue;
                    
                    var firstCandle = historyForPair.Value.History.First();
                    var lastCandle = historyForPair.Value.History.Last();

                    var marketSlice = new MarketSlice
                    {
                        AssetPairId = historyForPair.Key,
                        VolumeBase = historyForPair.Value.History.Sum(c => c.TradingVolume).ToString(CultureInfo.InvariantCulture),
                        VolumeQuote = historyForPair.Value.History.Sum(c => c.TradingOppositeVolume).ToString(CultureInfo.InvariantCulture),
                        PriceChange = firstCandle.Open > 0
                            ? ((lastCandle.Close - firstCandle.Open) / firstCandle.Open).ToString(CultureInfo.InvariantCulture)
                            : "0",
                        High = historyForPair.Value.History.Max(c => c.High).ToString(CultureInfo.InvariantCulture),
                        Low = historyForPair.Value.History.Min(c => c.Low).ToString(CultureInfo.InvariantCulture),
                    };

                    todayCandles.Add(marketSlice);
                }
            }

            return todayCandles;
        }
        
        private async Task<List<MarketSlice>> GetLastSpotCandlesAsync(string assetPairId = null)
        {
            var result = new List<MarketSlice>();

            if (!string.IsNullOrWhiteSpace(assetPairId))
            {
                var lastPriceSlice = await GetLastPriceSliceAsync(assetPairId);

                if (lastPriceSlice != null)
                {
                    result.Add(lastPriceSlice);
                }
            }
            else
            {
                var assetPairs = await _candlesHistoryClient.GetAvailableAssetPairsAsync();

                foreach (var assetPair in assetPairs)
                {
                    // it's OK to call it multiple times on init for now
                    var lastPriceSlice = await GetLastPriceSliceAsync(assetPair);

                    if (lastPriceSlice != null)
                    {
                        result.Add(lastPriceSlice);
                    }
                }
            }

            return result;
        }

        private async Task<MarketSlice> GetLastPriceSliceAsync(string assetPairId)
        {
            var tradesResponse = await _tradesAdapterClient.GetTradesByAssetPairIdAsync(assetPairId, 0, 1);

            if (tradesResponse != null && tradesResponse.Records.Any())
            {
                return new MarketSlice
                {
                    AssetPairId = assetPairId, 
                    LastPrice = tradesResponse.Records.First().Price.ToString(CultureInfo.InvariantCulture)
                };
            }

            return null;
        }
    }
}
