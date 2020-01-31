using System.Collections.Generic;
using Autofac;
using JetBrains.Annotations;
using Lykke.Common.Log;
using Lykke.Cqrs;
using Lykke.Cqrs.Configuration;
using Lykke.Cqrs.Middleware.Logging;
using Lykke.Exchange.Api.MarketData.Contract;
using Lykke.Exchange.Api.MarketData.Settings;
using Lykke.Messaging;
using Lykke.Messaging.RabbitMq;
using Lykke.Messaging.Serialization;
using Lykke.Service.Assets.Client;
using Lykke.SettingsReader;

namespace Lykke.Exchange.Api.MarketData.Modules
{
    [UsedImplicitly]
    public class CqrsModule : Module
    {
        private readonly IReloadingManager<AppSettings> _settings;

        public CqrsModule(IReloadingManager<AppSettings> settings)
        {
            _settings = settings;
        }

        protected override void Load(ContainerBuilder builder)
        {
            MessagePackSerializerFactory.Defaults.FormatterResolver = MessagePack.Resolvers.ContractlessStandardResolver.Instance;

            builder.Register(context => new AutofacDependencyResolver(context)).As<IDependencyResolver>().SingleInstance();

            var rabbitMqSettings = new RabbitMQ.Client.ConnectionFactory { Uri = _settings.CurrentValue.MarketDataService.Cqrs.ConnectionString };

            builder.Register(ctx =>
            {
                var logFactory = ctx.Resolve<ILogFactory>();
                var broker = rabbitMqSettings.Endpoint.ToString();
                var messagingEngine = new MessagingEngine(logFactory,
                    new TransportResolver(new Dictionary<string, TransportInfo>
                    {
                        {"RabbitMq", new TransportInfo(broker, rabbitMqSettings.UserName, rabbitMqSettings.Password, "None", "RabbitMq")}
                    }),
                    new RabbitMqTransportFactory(logFactory));

                var engine = new CqrsEngine(logFactory,
                    ctx.Resolve<IDependencyResolver>(),
                    messagingEngine,
                    new DefaultEndpointProvider(),
                    true,
                    Register.DefaultEndpointResolver(new RabbitMqConventionEndpointResolver(
                        "RabbitMq",
                        SerializationFormat.MessagePack,
                        environment: "lykke",
                        exclusiveQueuePostfix: "marketdata")),

                    Register.CommandInterceptors(new DefaultCommandLoggingInterceptor(logFactory)),
                    Register.EventInterceptors(new DefaultEventLoggingInterceptor(logFactory)),

                    Register.BoundedContext(MarketDataBoundedContext.Name)
                        .WithAssetsReadModel(route: System.Environment.MachineName)
                        .PublishingEvents(
                            typeof(MarketDataChangedEvent)
                        )
                        .With("events")
                );

                engine.StartPublishers();
                return engine;
            })
            .As<ICqrsEngine>()
            .AutoActivate()
            .SingleInstance();
        }
    }
}
