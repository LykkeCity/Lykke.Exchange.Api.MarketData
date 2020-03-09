using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Lykke.Exchange.Api.MarketData.Extensions;
using Lykke.Exchange.Api.MarketData.Models;
using Lykke.Exchange.Api.MarketData.Services;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.Controllers
{
    [Route("info")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class InfoController : Controller
    {
        private readonly IDatabase _database;
        private readonly TimeSpan _marketDataInterval;

        public InfoController(
            IDatabase database,
            RedisService redisService
            )
        {
            _database = database;
            _marketDataInterval = redisService.GetMarketDataInterval();
        }

        [HttpGet]
        public async Task<IActionResult> MarketData(string assetPair)
        {
            var data = await _database.HashGetAllAsync(RedisService.GetMarketDataKey(assetPair));
            var marketSlice = data?.ToMarketSlice() ?? new MarketSlice {AssetPairId = assetPair};

            var nowDate = DateTime.UtcNow;
            var now = nowDate.ToUnixTime();
            var from = (nowDate - _marketDataInterval).ToUnixTime();

            var baseVolumesDataTask = _database.SortedSetRangeByScoreAsync(RedisService.GetMarketDataBaseVolumeKey(assetPair), from, now);
            var quoteVolumesDataTask = _database.SortedSetRangeByScoreAsync(RedisService.GetMarketDataQuoteVolumeKey(assetPair), from, now);
            var pricesTask = _database.SortedSetRangeByScoreAsync(RedisService.GetMarketDataPriceKey(assetPair), from, now);

            await Task.WhenAll(baseVolumesDataTask, quoteVolumesDataTask, pricesTask);

            var prices = pricesTask.Result
                .Where(x => x.HasValue)
                .Select(x => RedisExtensions.DeserializeWithDate<decimal>(x)).ToList();

            var baseVolumes = baseVolumesDataTask.Result
                .Where(x => x.HasValue)
                .Select(x => RedisExtensions.DeserializeWithDate<decimal>(x)).ToList();

            decimal baseVolume = baseVolumes.Sum(x => x.data);

            var quoteVolumes = quoteVolumesDataTask.Result
                .Where(x => x.HasValue)
                .Select(x => RedisExtensions.DeserializeWithDate<decimal>(x)).ToList();

            decimal quoteVolume = quoteVolumes.Sum(x =>x.data);

            var pricesList = prices.Select(x => x.data).ToList();
            decimal high = pricesList.Any() ? pricesList.Max() : 0;
            decimal low = pricesList.Any() ? pricesList.Min() : 0;

            if (pricesList.Any())
            {
                decimal price = pricesList[0];

                if (price > 0)
                {
                    decimal priceChange = (decimal.Parse(marketSlice.LastPrice, CultureInfo.InvariantCulture) - price) / price;
                    marketSlice.PriceChange = priceChange.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (high > 0)
                marketSlice.High = high.ToString(CultureInfo.InvariantCulture);

            if (low > 0)
                marketSlice.Low = low.ToString(CultureInfo.InvariantCulture);

            marketSlice.VolumeBase = baseVolume.ToString(CultureInfo.InvariantCulture);
            marketSlice.VolumeQuote = quoteVolume.ToString(CultureInfo.InvariantCulture);

            var model = new MarketDataViewModel
            {
                From = nowDate - _marketDataInterval,
                To = nowDate,
                Slice = marketSlice,
                BaseVolumes = baseVolumes,
                QuoteVolumes = quoteVolumes,
                PriceValues = prices
            };

            return View(model);
        }
    }
}
