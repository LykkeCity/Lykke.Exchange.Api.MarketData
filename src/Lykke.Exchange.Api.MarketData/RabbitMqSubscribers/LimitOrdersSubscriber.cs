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
        private readonly ILog _log;
        private RabbitMqSubscriber<LimitOrdersMessage> _subscriber;

        public LimitOrdersSubscriber(
            IDatabase database,
            ICqrsEngine cqrsEngine,
            IAssetPairsReadModelRepository assetPairsRepository,
            ILogFactory logFactory,
            string connectionString,
            string exchangeName
        )
        {
            _database = database;
            _cqrsEngine = cqrsEngine;
            _assetPairsRepository = assetPairsRepository;
            _logFactory = logFactory;
            _connectionString = connectionString;
            _exchangeName = exchangeName;
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
                    continue;
                
                foreach (var tradeMessage in orderMessage.Trades.OrderBy(t => t.Timestamp).ThenBy(t => t.Index))
                {
                    string marketDataKey = RedisService.GetMarketDataKey(assetPairId);
                    string price = ((decimal)tradeMessage.Price).ToString(CultureInfo.InvariantCulture);
                    
                    await _database.HashSetAsync(marketDataKey, nameof(MarketSlice.LastPrice), price);
                    
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
                        double from = (nowDate - TimeSpan.FromHours(24)).ToUnixTime();
                        string baseVolumeKey = RedisService.GetMarketDataBaseVolumeKey(assetPairId);
                        string quoteVolumeKey = RedisService.GetMarketDataQuoteVolumeKey(assetPairId);
                        string priceKey = RedisService.GetMarketDataPriceKey(assetPairId);
                        
                        decimal baseVolumeSum = baseVolume;
                        decimal quoteVolumeSum = quotingVolume;
                        decimal priceChange = 0;
                        decimal high = (decimal)tradeMessage.Price;
                        decimal low = (decimal)tradeMessage.Price;
                        
                        var tasks = new List<Task>();
                        
                        var highValueTask = _database.HashGetAsync(marketDataKey, nameof(MarketSlice.High));
                        var lowValueTask = _database.HashGetAsync(marketDataKey, nameof(MarketSlice.Low));

                        await Task.WhenAll(highValueTask, lowValueTask);

                        if (highValueTask.Result.HasValue)
                        {
                            if ((double) highValueTask.Result < tradeMessage.Price)
                            {
                                tasks.Add(_database.HashSetAsync(marketDataKey, nameof(MarketSlice.High), price));
                            }
                            else
                            {
                                high = decimal.Parse(highValueTask.Result, CultureInfo.InvariantCulture);
                            }
                        }
                        else
                        {
                            tasks.Add(_database.HashSetAsync(marketDataKey, nameof(MarketSlice.High), price));
                        }

                        if (lowValueTask.Result.HasValue)
                        { 
                            if ((double) lowValueTask.Result > tradeMessage.Price)
                            {
                                tasks.Add(_database.HashSetAsync(marketDataKey, nameof(MarketSlice.Low), price));
                            }
                            else
                            {
                                low = decimal.Parse(lowValueTask.Result, CultureInfo.InvariantCulture);
                            }
                        }
                        else
                        {
                            tasks.Add(_database.HashSetAsync(marketDataKey, nameof(MarketSlice.Low), price));
                        }
                        
                        var baseVolumesDataTask = _database.SortedSetRangeByScoreAsync(baseVolumeKey, from, now);
                        var quoteVolumesDataTask = _database.SortedSetRangeByScoreAsync(quoteVolumeKey, from, now);

                        await Task.WhenAll(baseVolumesDataTask, quoteVolumesDataTask);
                        
                        foreach (var baseVolumeData in baseVolumesDataTask.Result)
                        {
                            if (!baseVolumeData.HasValue)
                                continue;
                            
                            decimal baseVol = RedisExtensions.DeserializeTimestamped<decimal>(baseVolumeData);
                            baseVolumeSum += baseVol;
                        }
                    
                        foreach (var quoteVolumeData in quoteVolumesDataTask.Result)
                        {
                            if (!quoteVolumeData.HasValue)
                                continue;

                            decimal quoteVol = RedisExtensions.DeserializeTimestamped<decimal>(quoteVolumeData);
                            quoteVolumeSum += quoteVol;
                        }
                        
                        var priceData = await _database.SortedSetRangeByScoreAsync(priceKey, intervalDate, intervalDate);

                        //new openPrice
                        if (!priceData.Any() || !priceData[0].HasValue)
                        {
                            tasks.Add(_database.SortedSetAddAsync(priceKey, RedisExtensions.SerializeWithTimestamp(price, interval), intervalDate));
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
                
                        tasks.Add(_database.HashSetAsync(marketDataKey, nameof(MarketSlice.VolumeBase), 
                        baseVolumeSum.ToString(CultureInfo.InvariantCulture)));
                        tasks.Add(_database.HashSetAsync(marketDataKey, nameof(MarketSlice.VolumeQuote), 
                        quoteVolumeSum.ToString(CultureInfo.InvariantCulture)));
                        
                        tasks.Add(_database.SortedSetAddAsync(baseVolumeKey, RedisExtensions.SerializeWithTimestamp(baseVolume, nowDate), now));
                        tasks.Add(_database.SortedSetAddAsync(quoteVolumeKey, RedisExtensions.SerializeWithTimestamp(quotingVolume, nowDate), now));
                        
                        tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(baseVolumeKey, 0, from, Exclude.Stop, CommandFlags.FireAndForget));
                        tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(quoteVolumeKey, 0, from, Exclude.Stop, CommandFlags.FireAndForget));

                        await Task.WhenAll(tasks);

                        var evt = new MarketDataChangedEvent
                        {
                            AssetPairId = assetPairId,
                            VolumeBase = baseVolumeSum,
                            VolumeQuote = quoteVolumeSum,
                            PriceChange = priceChange,
                            LastPrice = (decimal) tradeMessage.Price,
                            High = high,
                            Low = low
                        };

                        Console.WriteLine(evt.ToJson());
                        
                        _cqrsEngine.PublishEvent(evt, MarketDataBoundedContext.Name);
                    }
                }
            }
        }
    }
}
