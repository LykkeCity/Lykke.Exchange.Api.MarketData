using System;
using Autofac;
using Grpc.Core;
using JetBrains.Annotations;
using Lykke.Exchange.Api.MarketData.Services;
using Lykke.Exchange.Api.MarketData.Settings;
using Lykke.Logs;
using Lykke.Sdk;
using Lykke.Service.CandlesHistory.Client;
using Lykke.Service.MarketProfile.Client;
using Lykke.Service.TradesAdapter.Client;
using Lykke.SettingsReader;
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
                .SingleInstance();

            builder.RegisterType<ShutdownManager>()
                .As<IShutdownManager>()
                .AutoActivate()
                .SingleInstance();

            builder.RegisterInstance(
                new Server
                {
                    Services =
                    {
                        MarketDataService.BindService(new MarketDataServiceClient())
                    },
                    Ports = { new ServerPort("localhost", 5005, ServerCredentials.Insecure) }
                }
            );
            
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

            builder.RegisterTradesAdapterClient(_appSettings.CurrentValue.MarketDataService.TradesAdapterUrl, EmptyLogFactory.Instance.CreateLog(typeof(TradesAdapterClient)));
        }
    }
}
