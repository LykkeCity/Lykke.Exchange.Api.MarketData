using Autofac;
using Grpc.Core;
using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Contract
{
    [PublicAPI]
    public static class AutofacExtensions
    {
        /// <summary>
        ///     Registers <see cref="MarketDataService.MarketDataServiceClient" /> in Autofac container using
        ///     <see cref="MarketDataServiceClientSettings" />.
        /// </summary>
        /// <param name="builder">Autofac container builder.</param>
        /// <param name="settings"><see cref="MarketDataServiceClientSettings" /> client settings.</param>
        public static void RegisterMarketDataClient(this ContainerBuilder builder, MarketDataServiceClientSettings settings)
        {
            builder.Register(ctx =>
            {
                var channel = new Channel(settings.GrpcServiceUrl, SslCredentials.Insecure);
                return new MarketDataService.MarketDataServiceClient(channel);
            }).As<MarketDataService.MarketDataServiceClient>();
        }
    }
}
