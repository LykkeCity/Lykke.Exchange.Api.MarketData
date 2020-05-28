using System;
using MyNoSqlServer.DataWriter.Abstractions;

namespace Lykke.Exchange.Api.MarketData.Models
{
    public class Price : IMyNoSqlEntity
    {
        public Price()
        {
        }

        public Price(string assetPairId)
        {
            AssetPairId = assetPairId;
            PartitionKey = GetPk();
            RowKey = assetPairId;
            TimeStamp = DateTime.UtcNow;
        }

        public string AssetPairId { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTime TimeStamp { get; set; }
        public DateTime? Expires { get; set; }

        public static string GetPk() => "Price";
    }
}
