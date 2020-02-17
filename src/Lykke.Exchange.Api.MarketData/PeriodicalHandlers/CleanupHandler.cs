using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Common;
using JetBrains.Annotations;
using Lykke.Common.Log;
using Lykke.Exchange.Api.MarketData.Services;
using Lykke.Service.MarketProfile.Client;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.PeriodicalHandlers
{
    [UsedImplicitly]
    public class CleanupHandler : IStartable, IDisposable
    {
        private readonly ILykkeMarketProfile _marketProfileClient;
        private readonly IDatabase _database;
        private readonly TimerTrigger _timerTrigger;

        public CleanupHandler(
            ILykkeMarketProfile marketProfileClient,
            IDatabase database,
            ILogFactory logFactory
            )
        {
            _marketProfileClient = marketProfileClient;
            _database = database;
            _timerTrigger = new TimerTrigger(nameof(CleanupHandler), TimeSpan.FromMinutes(10), logFactory);
            _timerTrigger.Triggered += Execute;
        }

        public void Start()
        {
            _timerTrigger.Start();
        }

        public void Dispose()
        {
            _timerTrigger?.Stop();
            _timerTrigger?.Dispose();
        }

        private async Task Execute(ITimerTrigger timer, TimerTriggeredHandlerArgs args, CancellationToken cancellationToken)
        {
            double to = DateTime.UtcNow.AddHours(-24).ToUnixTime();
            var assetPairIds = (await _marketProfileClient.ApiMarketProfileGetAsync(cancellationToken))
                .Select(x => x.AssetPair)
                .ToList();

            var tasks = new List<Task>();

            foreach (var assetPairId in assetPairIds)
            {
                tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(RedisService.GetMarketDataBaseVolumeKey(assetPairId), 0, to, Exclude.Stop, CommandFlags.FireAndForget));
                tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(RedisService.GetMarketDataQuoteVolumeKey(assetPairId), 0, to, Exclude.Stop, CommandFlags.FireAndForget));
                tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(RedisService.GetMarketDataHighKey(assetPairId), 0, to, Exclude.Stop, CommandFlags.FireAndForget));
                tasks.Add(_database.SortedSetRemoveRangeByScoreAsync(RedisService.GetMarketDataLowKey(assetPairId), 0, to, Exclude.Stop, CommandFlags.FireAndForget));
            }

            await Task.WhenAll(tasks);
        }
    }
}
