using System;
using System.IO;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Lykke.Common;
using Microsoft.AspNetCore.Hosting;

namespace Lykke.Exchange.Api.MarketData
{
    [UsedImplicitly]
    public class Program
    {
        public static bool IsDebug;
        public static string EnvInfo => Environment.GetEnvironmentVariable("ENV_INFO");

        public static async Task Main(string[] args)
        {
#if DEBUG
            IsDebug = true;
#else
            IsDebug = false;
#endif
            Console.WriteLine($"{AppEnvironment.Name} version {AppEnvironment.Version}");
            Console.WriteLine(IsDebug ? "DEBUG mode" : "RELEASE mode");
            Console.WriteLine($"ENV_INFO: {EnvInfo}");

            try
            {
                var hostBuilder = new WebHostBuilder()
                    .UseKestrel()
                    .UseContentRoot(Directory.GetCurrentDirectory())
                    .UseStartup<Startup>();

                if (!IsDebug)
                    hostBuilder = hostBuilder.UseApplicationInsights();

                var host = hostBuilder.Build();

                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fatal error:");
                Console.WriteLine(ex);

                // Lets devops to see startup error in console between restarts in the Kubernetes
                var delay = TimeSpan.FromMinutes(1);

                Console.WriteLine();
                Console.WriteLine($"Process will be terminated in {delay}. Press any key to terminate immediately.");

                Task.WhenAny(
                        Task.Delay(delay),
                        Task.Run(() =>
                        {
                            Console.ReadKey(true);
                        }))
                    .Wait();
            }

            Console.WriteLine("Terminated");
        }
    }
}
