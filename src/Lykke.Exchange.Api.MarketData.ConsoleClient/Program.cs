using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Lykke.Exchange.Api.MarketData.ConsoleClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting GRPC Client...");
            Console.WriteLine();

            var channel = new Channel("localhost:5005", ChannelCredentials.Insecure);
            var client = new MarketDataService.MarketDataServiceClient(channel);

            try
            {
                var sw = new Stopwatch();
                sw.Start();
                var res = await client.GetMarketDataAsync(new Empty());
                sw.Stop();

                Console.WriteLine($"GetAll = {sw.ElapsedMilliseconds} msec");
                
                sw.Reset();
                
                sw.Start();
                MarketSlice response = await client.GetAssetPairMarketDataAsync(new MarketDataRequest{ AssetPairId = "BTCUSD" });
                sw.Stop();

                Console.WriteLine($"GetAll = {sw.ElapsedMilliseconds} msec");
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
