using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Common;
using Common.Log;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Exchange.Api.MarketData.Contract;
using Lykke.Exchange.Api.MarketData.Extensions;
using Lykke.Exchange.Api.MarketData.RabbitMqSubscribers.Messages;
using Lykke.Exchange.Api.MarketData.Services;
using Lykke.Job.CandlesProducer.Contract;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.Service.Assets.Client.Models.v3;
using Lykke.Service.Assets.Client.ReadModels;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.RabbitMqSubscribers
{
    public class LimitOrdersSubscriber : IStartable, IDisposable
    {
        private readonly IDatabase _database;
        private readonly ICqrsEngine _cqrsEngine;
        private readonly IAssetPairsReadModelRepository _assetPairsRepository;
        private readonly ILogFactory _logFactory;
        private readonly string _connectionString;
        private readonly string _exchangeName;
        private readonly TimeSpan _marketDataInterval;
        private readonly ILog _log;
        private RabbitMqSubscriber<LimitOrdersMessage> _subscriber;

        public LimitOrdersSubscriber(
            IDatabase database,
            ICqrsEngine cqrsEngine,
            IAssetPairsReadModelRepository assetPairsRepository,
            ILogFactory logFactory,
            string connectionString,
            string exchangeName,
            TimeSpan marketDataInterval
        )
        {
            _database = database;
            _cqrsEngine = cqrsEngine;
            _assetPairsRepository = assetPairsRepository;
            _logFactory = logFactory;
            _connectionString = connectionString;
            _exchangeName = exchangeName;
            _marketDataInterval = marketDataInterval;
            _log = logFactory.CreateLog(this);
        }

        public void Start()
        {
            var settings = RabbitMqSubscriptionSettings
                .ForSubscriber(_connectionString, "lykke", _exchangeName, "lykke", "marketdata");

            settings.DeadLetterExchangeName = null;

            try
            {
                _subscriber = new RabbitMqSubscriber<LimitOrdersMessage>(_logFactory, settings,
                        new ResilientErrorHandlingStrategy(_logFactory, settings,
                            retryTimeout: TimeSpan.FromSeconds(10),
                            retryNum: 10,
                            next: new DeadQueueErrorHandlingStrategy(_logFactory, settings)))
                    .SetMessageDeserializer(new JsonMessageDeserializer<LimitOrdersMessage>())
                    .SetMessageReadStrategy(new MessageReadQueueStrategy())
                    .Subscribe(ProcessLimitOrdersAsync)
                    .CreateDefaultBinding()
                    .Start();
            }
            catch (Exception ex)
            {
                _log.Error(nameof(Start), ex);
                throw;
            }
        }

        public void Dispose()
        {
            _subscriber?.Stop();
        }

        private async Task ProcessLimitOrdersAsync(LimitOrdersMessage message)
        {
            if (message.Orders == null || !message.Orders.Any())
                return;

            HashSet<string> limitOrderIds = message.Orders
                .Select(o => o.Order.Id)
                .ToHashSet();

            foreach (var orderMessage in message.Orders)
            {
                if (orderMessage.Trades == null || !orderMessage.Trades.Any())
                    continue;

                string assetPairId = orderMessage.Order.AssetPairId;

                AssetPair assetPair = _assetPairsRepository.TryGet(assetPairId);

                if (assetPair == null)
                {
                    _log.Error($"Asset pair {assetPairId} not found");
                    continue;
                }

                List<LimitOrdersMessage.Trade> allTrades = message.Orders.SelectMany(x => x.Trades).ToList();

                string marketDataKey = RedisService.GetMarketDataKey(assetPairId);
                string baseVolumeKey = RedisService.GetMarketDataBaseVolumeKey(assetPairId);
                string quoteVolumeKey = RedisService.GetMarketDataQuoteVolumeKey(assetPairId);
                string priceKey = RedisService.GetMarketDataOpenPriceKey(assetPairId);
                string highKey = RedisService.GetMarketDataHighKey(assetPairId);
                string lowKey = RedisService.GetMarketDataLowKey(assetPairId);
                decimal highValue = 0;
                decimal lowValue = 0;
                var highLowDateTime = DateTime.UtcNow;

                foreach (var tradeMessage in orderMessage.Trades.OrderBy(t => t.Timestamp).ThenBy(t => t.Index))
                {
                    long maxIndex = allTrades
                        .Where(x => x.OppositeOrderId == tradeMessage.OppositeOrderId)
                        .Max(t => t.Index);

                    var price = (decimal)tradeMessage.Price;
                    string priceString = price.ToString(CultureInfo.InvariantCulture);

                    await _database.HashSetAsync(marketDataKey, nameof(MarketSlice.LastPrice), priceString);

                    var isOppositeOrderIsLimit = limitOrderIds.Contains(tradeMessage.OppositeOrderId);
                    // If opposite order is market order, then unconditionally takes the given limit order.
                    // But if both of orders are limit orders, we should take only one of them.
                    if (isOppositeOrderIsLimit)
                    {
                        var isBuyOrder = orderMessage.Order.Volume > 0;
                        // Takes trade only for the sell limit orders
                        if (isBuyOrder)
                        {
                            continue;
                        }
                    }

                    decimal baseVolume;
                    decimal quotingVolume;

                    if (tradeMessage.Asset == assetPair.BaseAssetId)
                    {
                        baseVolume = (decimal)tradeMessage.Volume;
                        quotingVolume = (decimal)tradeMessage.OppositeVolume;
                    }
                    else
                    {
                        baseVolume = (decimal)tradeMessage.OppositeVolume;
                        quotingVolume = (decimal)tradeMessage.Volume;
                    }

                    if (tradeMessage.Price > 0 && baseVolume > 0 && quotingVolume > 0)
                    {
                        var nowDate = tradeMessage.Timestamp;
                        DateTime interval = nowDate.TruncateTo(CandleTimeInterval.Min5);
                        double intervalDate = interval.ToUnixTime();
                        double now = nowDate.ToUnixTime();
                        double from = (nowDate - _marketDataInterval).ToUnixTime();

                        decimal baseVolumeSum = baseVolume;
                        decimal quoteVolumeSum = quotingVolume;
                        decimal priceChange = 0;
                        highValue = (decimal)tradeMessage.Price;
                        lowValue = (decimal)tradeMessage.Price;
                        highLowDateTime = nowDate;

                        var tasks = new List<Task>();

                        var baseVolumesDataTask = _database.SortedSetRangeByScoreAsync(baseVolumeKey, from, now);
                        var quoteVolumesDataTask = _database.SortedSetRangeByScoreAsync(quoteVolumeKey, from, now);
                        var highListTask = _database.SortedSetRangeByScoreAsync(highKey, from, now);
                        var lowListTask = _database.SortedSetRangeByScoreAsync(lowKey, from, now);

                        await Task.WhenAll(baseVolumesDataTask, quoteVolumesDataTask, highListTask, lowListTask);

                        baseVolumeSum += baseVolumesDataTask.Result
                            .Where(x => x.HasValue)
                            .Sum(x => RedisExtensions.DeserializeTimestamped<decimal>(x));

                        quoteVolumeSum += quoteVolumesDataTask.Result
                            .Where(x => x.HasValue)
                            .Sum(x => RedisExtensions.DeserializeTimestamped<decimal>(x));

                        var currentHigh = highListTask.Result.Any(x => x.HasValue)
                            ? highListTask.Result
                                .Where(x => x.HasValue)
                                .Max(x => RedisExtensions.DeserializeTimestamped<decimal>(x))
                            : (decimal?) null;

                        if (currentHigh.HasValue && currentHigh.Value > highValue)
                            highValue = currentHigh.Value;

                        var currentLow = lowListTask.Result.Any(x => x.HasValue)
                            ? lowListTask.Result
                                .Where(x => x.HasValue)
                                .Min(x => RedisExtensions.DeserializeTimestamped<decimal>(x))
                            : (decimal?)null;

                        if (currentLow.HasValue && currentLow.Value < lowValue)
                            lowValue = currentLow.Value;

                        var priceData = await _database.SortedSetRangeByScoreAsync(priceKey, intervalDate, intervalDate);

                        //new openPrice
                        if (!priceData.Any() || !priceData[0].HasValue)
                        {
                            tasks.Add(_database.SortedSetAddAsync(priceKey, RedisExtensions.SerializeWithTimestamp(priceString, interval), intervalDate));
                            tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(priceKey, 0, from, Exclude.Stop, CommandFlags.FireAndForget));
                        }

                        var pricesData = await _database.SortedSetRangeByScoreAsync(priceKey, from, now);

                        if (pricesData.Any() && pricesData[0].HasValue)
                        {
                            decimal openPrice = RedisExtensions.DeserializeTimestamped<decimal>(pricesData[0]);

                            if (openPrice > 0)
                                priceChange = ((decimal)tradeMessage.Price - openPrice) / openPrice;
                        }

                        if (priceChange != 0)
                        {
                            tasks.Add(_database.HashSetAsync(marketDataKey, nameof(MarketSlice.PriceChange),
                                priceChange.ToString(CultureInfo.InvariantCulture)));
                        }

                        tasks.Add(_database.HashSetAsync(marketDataKey, nameof(MarketSlice.VolumeBase), baseVolumeSum.ToString(CultureInfo.InvariantCulture)));
                        tasks.Add(_database.HashSetAsync(marketDataKey, nameof(MarketSlice.VolumeQuote), quoteVolumeSum.ToString(CultureInfo.InvariantCulture)));

                        var nowTradeDate = nowDate.AddMilliseconds(tradeMessage.Index);

                        tasks.Add(_database.SortedSetAddAsync(baseVolumeKey, RedisExtensions.SerializeWithTimestamp(baseVolume, nowTradeDate), now));
                        tasks.Add(_database.SortedSetAddAsync(quoteVolumeKey, RedisExtensions.SerializeWithTimestamp(quotingVolume, nowTradeDate), now));

                        await Task.WhenAll(tasks);

                        //send event only for the last trade in the order
                        if (tradeMessage.Index == maxIndex)
                        {
                            var evt = new MarketDataChangedEvent
                            {
                                AssetPairId = assetPairId,
                                VolumeBase = baseVolumeSum,
                                VolumeQuote = quoteVolumeSum,
                                PriceChange = priceChange,
                                LastPrice = (decimal) tradeMessage.Price,
                                High = highValue,
                                Low = lowValue
                            };

                            _log.Info($"Send MarketDataChangedEvent for trade index = {tradeMessage.Index}", context: evt.ToJson());

                            _cqrsEngine.PublishEvent(evt, MarketDataBoundedContext.Name);
                        }
                    }
                }

                await Task.WhenAll(
                    _database.HashSetAsync(marketDataKey, nameof(MarketSlice.High), highValue.ToString(CultureInfo.InvariantCulture)),
                    _database.HashSetAsync(marketDataKey, nameof(MarketSlice.Low), lowValue.ToString(CultureInfo.InvariantCulture)),
                    _database.SortedSetAddAsync(highKey, RedisExtensions.SerializeWithTimestamp(highValue, highLowDateTime), highLowDateTime.ToUnixTime()),
                    _database.SortedSetAddAsync(lowKey, RedisExtensions.SerializeWithTimestamp(lowValue, highLowDateTime), highLowDateTime.ToUnixTime())
                );
            }
        }
    }
}
