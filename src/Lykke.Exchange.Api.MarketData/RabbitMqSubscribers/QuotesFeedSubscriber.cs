using System;
using System.Globalization;
using System.Threading.Tasks;
using Autofac;
using Common.Log;
using Lykke.Common.Log;
using Lykke.Exchange.Api.MarketData.Models;
using Lykke.Exchange.Api.MarketData.Services;
using Lykke.Job.QuotesProducer.Contract;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Subscriber;
using MyNoSqlServer.DataWriter.Abstractions;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.RabbitMqSubscribers
{
    public class QuotesFeedSubscriber : IStartable, IDisposable
    {
        private readonly IDatabase _database;
        private readonly ILogFactory _logFactory;
        private readonly string _connectionString;
        private readonly string _exchangeName;
        private readonly IMyNoSqlServerDataWriter<Price> _priceWriter;
        private readonly ILog _log;
        private RabbitMqSubscriber<QuoteMessage> _subscriber;

        public QuotesFeedSubscriber(
            IDatabase database,
            ILogFactory logFactory,
            string connectionString,
            string exchangeName,
            IMyNoSqlServerDataWriter<Price> priceWriter
            )
        {
            _database = database;
            _logFactory = logFactory;
            _connectionString = connectionString;
            _exchangeName = exchangeName;
            _priceWriter = priceWriter;
            _log = logFactory.CreateLog(this);
        }

        public void Start()
        {
            var settings = RabbitMqSubscriptionSettings
                .ForSubscriber(_connectionString, "lykke", _exchangeName, "lykke", $"MarketData-{nameof(QuotesFeedSubscriber)}");

            settings.DeadLetterExchangeName = null;

            try
            {
                _subscriber = new RabbitMqSubscriber<QuoteMessage>(_logFactory, settings,
                        new ResilientErrorHandlingStrategy(_logFactory, settings,
                            retryTimeout: TimeSpan.FromSeconds(10),
                            retryNum: 10,
                            next: new DeadQueueErrorHandlingStrategy(_logFactory, settings)))
                    .SetMessageDeserializer(new JsonMessageDeserializer<QuoteMessage>())
                    .SetMessageReadStrategy(new MessageReadQueueStrategy())
                    .Subscribe(ProcessQuoteAsync)
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

        private async Task ProcessQuoteAsync(QuoteMessage quote)
        {
            var entity = await _priceWriter.GetAsync(Price.GetPk(), quote.AssetPair);

            var writerTask = Task.CompletedTask;

            if (entity != null)
            {
                if (quote.IsBuy)
                    entity.Bid = (decimal) quote.Price;
                else
                    entity.Ask = (decimal) quote.Price;

                entity.TimeStamp = DateTime.UtcNow;

                writerTask = _priceWriter.InsertOrReplaceAsync(entity);
            }

            await Task.WhenAll(
                _database.HashSetAsync(RedisService.GetMarketDataKey(quote.AssetPair),
                    quote.IsBuy ? nameof(MarketSlice.Bid) : nameof(MarketSlice.Ask),
                    quote.Price.ToString(CultureInfo.InvariantCulture)),
                writerTask);
        }
    }
}
