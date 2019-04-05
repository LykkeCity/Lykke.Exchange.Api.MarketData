using Autofac;
using Lykke.Exchange.Api.MarketData.Services;
using Lykke.Exchange.Api.MarketData.Settings;
using Lykke.SettingsReader;

namespace Lykke.Exchange.Api.MarketData.Modules
{
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
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<ShutdownManager>()
                .AsSelf()
                .AutoActivate()
                .SingleInstance();
        }
    }
}
