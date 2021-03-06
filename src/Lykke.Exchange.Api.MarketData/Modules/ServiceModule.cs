using System;
using Autofac;
using Grpc.Core;
using Grpc.Reflection;
using Grpc.Reflection.V1Alpha;
using JetBrains.Annotations;
using Lykke.Exchange.Api.MarketData.Models;
using Lykke.Exchange.Api.MarketData.PeriodicalHandlers;
using Lykke.Exchange.Api.MarketData.RabbitMqSubscribers;
using Lykke.Exchange.Api.MarketData.Services;
using Lykke.Exchange.Api.MarketData.Settings;
using Lykke.Sdk;
using Lykke.Service.Assets.Client;
using Lykke.Service.CandlesHistory.Client;
using Lykke.Service.MarketProfile.Client;
using Lykke.SettingsReader;
using MyNoSqlServer.DataWriter.Abstractions;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.Modules
{
    [UsedImplicitly]
    public class ServiceModule : Module
    {
        private readonly IReloadingManager<AppSettings> _appSettings;

        public ServiceModule(IReloadingManager<AppSettings> appSettings)
        {
            _appSettings = appSettings;
        }

        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<StartupManager>()
                .As<IStartupManager>()
                .AutoActivate()
                .SingleInstance();

            builder.RegisterType<ShutdownManager>()
                .As<IShutdownManager>()
                .AutoActivate()
                .SingleInstance();

            builder.Register(ctx =>
                {
                    var reflectionServiceImpl = new ReflectionServiceImpl(
                        MarketDataService.Descriptor
                    );
                    return new Server
                        {
                            Services =
                                {
                                    MarketDataService.BindService(
                                        new MarketDataServiceClient(ctx.Resolve<RedisService>())),
                                    ServerReflection.BindService(reflectionServiceImpl)
                                },
                            Ports =
                                {
                                    new ServerPort("0.0.0.0", _appSettings.CurrentValue.MarketDataService.GrpcPort,
                                        ServerCredentials.Insecure)
                                }
                        };
                }
            ).SingleInstance();

            builder.Register(c =>
                {
                    var lazy = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(_appSettings.CurrentValue.MarketDataService.Redis.Configuration));
                    return lazy.Value;
                })
                .As<IConnectionMultiplexer>()
                .SingleInstance();

            builder.Register(c => c.Resolve<IConnectionMultiplexer>().GetDatabase())
                .As<IDatabase>();

            builder.RegisterMarketProfileClient(_appSettings.CurrentValue.MarketDataService.MarketProfileUrl);

            builder.RegisterInstance(
                new Candleshistoryservice(new Uri(_appSettings.CurrentValue.MarketDataService.CandlesHistoryUrl))
            ).As<ICandleshistoryservice>().SingleInstance();

            builder.RegisterType<InitService>()
                .WithParameter(TypedParameter.From(_appSettings.CurrentValue.MarketDataService.MarketDataInterval))
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<QuotesFeedSubscriber>()
                .WithParameter(new NamedParameter("connectionString", _appSettings.CurrentValue.MarketDataService.RabbitMq.QuotesConnectionString))
                .WithParameter(new NamedParameter("exchangeName", _appSettings.CurrentValue.MarketDataService.RabbitMq.QuotesExchangeName))
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<LimitOrdersSubscriber>()
                .WithParameter(new NamedParameter("connectionString", _appSettings.CurrentValue.MarketDataService.RabbitMq.LimitOrdersConnectionString))
                .WithParameter(new NamedParameter("exchangeName", _appSettings.CurrentValue.MarketDataService.RabbitMq.LimitOrdersExchangeName))
                .WithParameter(TypedParameter.From(_appSettings.CurrentValue.MarketDataService.MarketDataInterval))
                .AsSelf()
                .SingleInstance();

            builder.RegisterAssetsClient(_appSettings.CurrentValue.AssetsServiceClient);

            builder.RegisterType<RedisService>()
                .WithParameter(TypedParameter.From(_appSettings.CurrentValue.MarketDataService.MarketDataInterval))
                .SingleInstance();

            builder.RegisterType<CleanupHandler>()
                .WithParameter(TypedParameter.From(_appSettings.CurrentValue.MarketDataService.MarketDataInterval))
                .As<IStartable>()
                .AutoActivate()
                .SingleInstance();

            builder.Register(ctx =>
            {
                return _appSettings.CurrentValue.MarketDataService.MyNoSqlServer.Enabled
                    ? new MyNoSqlServer.DataWriter.MyNoSqlServerDataWriter<Ticker>(() =>
                            _appSettings.CurrentValue.MarketDataService.MyNoSqlServer.ServiceUrl,
                        _appSettings.CurrentValue.MarketDataService.MyNoSqlServer.TickersTableName)
                    : (IMyNoSqlServerDataWriter<Ticker>) new MockNoSqlServerDataWriter<Ticker>();
            }).As<IMyNoSqlServerDataWriter<Ticker>>().SingleInstance();

            builder.Register(ctx =>
            {
                return _appSettings.CurrentValue.MarketDataService.MyNoSqlServer.Enabled
                    ? new MyNoSqlServer.DataWriter.MyNoSqlServerDataWriter<Price>(() =>
                        _appSettings.CurrentValue.MarketDataService.MyNoSqlServer.ServiceUrl,
                    _appSettings.CurrentValue.MarketDataService.MyNoSqlServer.PricesTableName)
                    : (IMyNoSqlServerDataWriter<Price>) new MockNoSqlServerDataWriter<Price>();
            }).As<IMyNoSqlServerDataWriter<Price>>().SingleInstance();
        }
    }
}
