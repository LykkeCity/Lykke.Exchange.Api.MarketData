using System;
using System.Threading.Tasks;
using Grpc.Core;

namespace Lykke.Exchange.Api.MarketData.ConsoleClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting GRPC Client...");
            Console.WriteLine();

            var channel = new Channel("localhost:5005", SslCredentials.Insecure);
            var client = new MarketDataService.MarketDataServiceClient(channel);

            try
            {
                var response = await client.GetMarketDataAsync(new MarketDataRequest{ AssetPairId = "BTCUSD" });

                Console.WriteLine("Asset pair: " + response.AssetPairId);
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"error: {ex.Message}");
            }

            Console.WriteLine();
            Console.WriteLine("Shutting down");
            await channel.ShutdownAsync();

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
