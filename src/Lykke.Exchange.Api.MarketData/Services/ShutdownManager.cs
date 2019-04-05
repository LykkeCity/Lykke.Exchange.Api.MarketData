using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Lykke.Exchange.Api.MarketData.Services
{
    [UsedImplicitly]
    public class ShutdownManager
    {
        public Task StopAsync()
        {
            return Task.CompletedTask;
        }
    }
}
