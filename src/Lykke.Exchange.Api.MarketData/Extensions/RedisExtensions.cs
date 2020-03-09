using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using StackExchange.Redis;

namespace Lykke.Exchange.Api.MarketData.Extensions
{
    public static class RedisExtensions
    {
        public static HashEntry[] ToHashEntries(this object obj)
        {
            PropertyInfo[] properties = obj.GetType().GetProperties();
            var skipNames = new[] {"Parser", "Descriptor"};

            return properties
                .Where(x => x.GetValue(obj) != null && !skipNames.Contains(x.Name))
                .Select(property => new HashEntry(property.Name, property.GetValue(obj)
                    .ToString())).ToArray();
        }

        public static HashEntry[] ToMarketSliceHash(this MarketSlice marketSlice)
        {
            return new[]
            {
                new HashEntry(nameof(marketSlice.AssetPairId), marketSlice.AssetPairId),
                new HashEntry(nameof(marketSlice.LastPrice), marketSlice.LastPrice),
                new HashEntry(nameof(marketSlice.Bid), marketSlice.Bid),
                new HashEntry(nameof(marketSlice.Ask), marketSlice.Ask)
            };
        }

        public static MarketSlice ToMarketSlice(this HashEntry[] hashEntry)
        {
            var marketSlice = new MarketSlice();

            var hashDict = hashEntry.ToDictionary();

            if (hashDict.TryGetValue(nameof(MarketSlice.AssetPairId), out var assetPair))
                marketSlice.AssetPairId = assetPair;

            if (hashDict.TryGetValue(nameof(MarketSlice.LastPrice), out var lastPrice))
                marketSlice.LastPrice = lastPrice;

            if (hashDict.TryGetValue(nameof(MarketSlice.Bid), out var bid))
                marketSlice.Bid = bid;

            if (hashDict.TryGetValue(nameof(MarketSlice.Ask), out var ask))
                marketSlice.Ask = ask;

            return marketSlice;
        }

        public static byte[] SerializeWithTimestamp<T>(T data, DateTime date)
        {
            // result is:
            // 0 .. TimestampFormat.Length - 1 bytes: timestamp as yyyyMMddHHmmssfff in ASCII
            // TimestampFormat.Length .. end bytes: serialized data

            var timestampString = date.ToString("yyyyMMddHHmmssfff");
            var timestampBytes = Encoding.ASCII.GetBytes(timestampString);

            using (var stream = new MemoryStream())
            {
                stream.Write(timestampBytes, 0, timestampBytes.Length);

                MessagePack.MessagePackSerializer.Serialize(stream, data);

                stream.Flush();

                return stream.ToArray();
            }
        }

        public static T DeserializeTimestamped<T>(byte[] value)
        {
            // value is:
            // 0 .. TimestampFormat.Length - 1 bytes: timestamp as yyyyMMddHHmmss in ASCII
            // TimestampFormat.Length .. end bytes: serialized data

            var timestampLength = "yyyyMMddHHmmssfff".Length;

            using (var stream = new MemoryStream(value, timestampLength, value.Length - timestampLength, writable: false))
            {
                return MessagePack.MessagePackSerializer.Deserialize<T>(stream);
            }
        }

        public static (T data, string dateTime) DeserializeWithDate<T>(byte[] value)
        {
            // value is:
            // 0 .. TimestampFormat.Length - 1 bytes: timestamp as yyyyMMddHHmmss in ASCII
            // TimestampFormat.Length .. end bytes: serialized data

            var timestampLength = "yyyyMMddHHmmssfff".Length;
            T data;
            var str = Encoding.UTF8.GetString(value).Substring(0, timestampLength);
            var dateTime = DateTime.ParseExact(str, "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture).ToString("u");

            using (var stream = new MemoryStream(value, timestampLength, value.Length - timestampLength, writable: false))
            {
                data = MessagePack.MessagePackSerializer.Deserialize<T>(stream);
            }

            return (data, dateTime);
        }
    }
}
