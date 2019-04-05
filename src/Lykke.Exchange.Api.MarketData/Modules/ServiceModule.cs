using Autofac;
using Grpc.Core;
using JetBrains.Annotations;
using Lykke.Exchange.Api.MarketData.Services;
using Lykke.Exchange.Api.MarketData.Settings;
using Lykke.Sdk;
using Lykke.SettingsReader;

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
        }
    }
}
