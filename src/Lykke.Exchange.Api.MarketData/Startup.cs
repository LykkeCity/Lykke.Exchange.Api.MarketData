using System;
using System.Threading.Tasks;
using AsyncFriendlyStackTrace;
using Autofac;
using Common.Log;
using Lykke.Common;
using Lykke.Common.Api.Contract.Responses;
using Lykke.Common.ApiLibrary.Middleware;
using Lykke.Common.Log;
using Lykke.Exchange.Api.MarketData.Modules;
using Lykke.Exchange.Api.MarketData.Services;
using Lykke.Exchange.Api.MarketData.Settings;
using Lykke.Logs;
using Lykke.MonitoringServiceApiCaller;
using Lykke.SettingsReader;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lykke.Exchange.Api.MarketData
{
    public class Startup
    {
        private string _monitoringServiceUrl;
        private ILog _log;
        private IHealthNotifier _healthNotifier;
        private IReloadingManager<AppSettings> _appSettings;
        
        private IContainer ApplicationContainer { get; set; }
        private IConfigurationRoot Configuration { get; }
        
        public Startup(IWebHostEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.RegisterModule(new ServiceModule(_appSettings));
            ApplicationContainer = builder.Build();
            
            var logFactory = ApplicationContainer.Resolve<ILogFactory>();
            _log = logFactory.CreateLog(this);
            _healthNotifier = ApplicationContainer.Resolve<IHealthNotifier>();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            try
            {
                services.AddGrpc();

                services.AddMvc()
                    .AddNewtonsoftJson(options =>
                    {
                        options.SerializerSettings.ContractResolver =
                            new Newtonsoft.Json.Serialization.DefaultContractResolver();
                    });

                _appSettings = Configuration.LoadSettings<AppSettings>(o =>
                {
                    o.SetConnString(s => s.SlackNotifications.AzureQueue.ConnectionString);
                    o.SetQueueName(s => s.SlackNotifications.AzureQueue.QueueName);
                    o.SenderName = $"{AppEnvironment.Name} {AppEnvironment.Version}";
                });

                if (_appSettings.CurrentValue.MonitoringServiceClient != null)
                    _monitoringServiceUrl = _appSettings.CurrentValue.MonitoringServiceClient.MonitoringServiceUrl;

                services.AddLykkeLogging(
                    _appSettings.ConnectionString(s => s.MarketDataService.Db.LogsConnString),
                    "MarketDataLogs",
                    _appSettings.CurrentValue.SlackNotifications.AzureQueue.ConnectionString,
                    _appSettings.CurrentValue.SlackNotifications.AzureQueue.QueueName);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime appLifetime)
        {
            try
            {
                if (env.IsDevelopment())
                    app.UseDeveloperExceptionPage();

                app.UseLykkeForwardedHeaders();
                app.UseLykkeMiddleware(ex => new ErrorResponse {ErrorMessage = ex.ToAsyncString()});

                app.UseRouting(routes =>
                {
                    routes.MapGrpcService<MarketDataServiceClient>();
                    routes.MapControllers();
                });

                appLifetime.ApplicationStarted.Register(() => StartApplication().GetAwaiter().GetResult());
                appLifetime.ApplicationStopping.Register(() => StopApplication().GetAwaiter().GetResult());
                appLifetime.ApplicationStopped.Register(CleanUp);
            }
            catch (Exception ex)
            {
                _log?.Critical(ex);
                throw;
            }
        }
        
        private async Task StartApplication()
        {
            try
            {
                await ApplicationContainer.Resolve<StartupManager>().StartAsync();
                
#if !DEBUG
                await Configuration.RegisterInMonitoringServiceAsync(_monitoringServiceUrl, _healthNotifier);
#endif
            }
            catch (Exception ex)
            {
                _log.Critical(ex);
                throw;
            }
        }

        private async Task StopApplication()
        {
            try
            {
                await ApplicationContainer.Resolve<ShutdownManager>().StopAsync();
            }
            catch (Exception ex)
            {
                _log?.Critical(ex);
                throw;
            }
        }

        private void CleanUp()
        {
            try
            {
                _healthNotifier?.Notify("Terminating");

                ApplicationContainer.Dispose();
            }
            catch (Exception ex)
            {
                _log?.Critical(ex);
                throw;
            }
        }
    }
}
