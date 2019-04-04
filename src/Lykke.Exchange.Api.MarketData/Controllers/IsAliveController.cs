using Microsoft.AspNetCore.Mvc;

namespace Lykke.Exchange.Api.MarketData.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class IsAliveController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok("OK");
        }
    }
}
