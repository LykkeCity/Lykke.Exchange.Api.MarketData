using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Lykke.Exchange.Api.MarketData.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Lykke.Exchange.Api.MarketData.Controllers
{
    [Route("api/[Controller]")]
    public class MarketDataController : Controller
    {
        private readonly RedisService _redisService;

        public MarketDataController(RedisService redisService)
        {
            _redisService = redisService;
        }
        
        [HttpGet]
        [SwaggerOperation("GetMarketData")]
        [ProducesResponseType(typeof(List<MarketSlice>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetMarketData([FromQuery]string assetPairId)
        {
            var result = new List<MarketSlice>();

            if (string.IsNullOrEmpty(assetPairId))
            {
                var data = await _redisService.GetMarketDataAsync();
                result.AddRange(data);
            }
            else
            {
                var marketSlice = await _redisService.GetMarketDataAsync(assetPairId);
                
                if (marketSlice != null)
                    result.Add(marketSlice);
            }
            
            return Ok(result);
        }
    }
}
