namespace Lykke.Exchange.Api.MarketData.Settings
{
    public class MyNoSqlSettings
    {
        public string ServiceUrl { get; set; }
        public string TickersTableName { get; set; }
        public string PricesTableName { get; set; }
        public bool Enabled { get; set; }
    }
}
