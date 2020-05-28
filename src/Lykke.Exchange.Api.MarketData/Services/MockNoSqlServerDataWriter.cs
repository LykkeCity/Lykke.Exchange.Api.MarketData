using System.Collections.Generic;
using System.Threading.Tasks;
using MyNoSqlServer.DataWriter.Abstractions;

namespace Lykke.Exchange.Api.MarketData.Services
{
    public class MockNoSqlServerDataWriter<T> : IMyNoSqlServerDataWriter<T> where T : IMyNoSqlEntity, new()
    {
        public Task InsertAsync(T entity) => Task.CompletedTask;
        public Task InsertOrReplaceAsync(T entity) => Task.CompletedTask;
        public Task CleanAndKeepLastRecordsAsync(string partitionKey, int amount) => Task.CompletedTask;
        public Task BulkInsertOrReplaceAsync(IEnumerable<T> entity, DataSynchronizationPeriod dataSynchronizationPeriod = DataSynchronizationPeriod.Sec5) => Task.CompletedTask;
        public Task CleanAndBulkInsertAsync(IEnumerable<T> entity, DataSynchronizationPeriod dataSynchronizationPeriod = DataSynchronizationPeriod.Sec5) => Task.CompletedTask;
        public Task CleanAndBulkInsertAsync(string partitionKey, IEnumerable<T> entity,
            DataSynchronizationPeriod dataSynchronizationPeriod = DataSynchronizationPeriod.Sec5) => Task.CompletedTask;
        public Task<IEnumerable<T>> GetAsync() => Task.FromResult(default(IEnumerable<T>));
        public Task<IEnumerable<T>> GetAsync(string partitionKey) => Task.FromResult(default(IEnumerable<T>));
        public Task<T> GetAsync(string partitionKey, string rowKey) => Task.FromResult(default(T));
        public Task<IReadOnlyList<T>> GetMultipleRowKeysAsync(string partitionKey, IEnumerable<string> rowKeys) =>Task.FromResult(default(IReadOnlyList<T>));
        public Task<T> DeleteAsync(string partitionKey, string rowKey) => Task.FromResult(default(T));
        public Task<IEnumerable<T>> QueryAsync(string query) => Task.FromResult(default(IEnumerable<T>));
        public Task<IEnumerable<T>> GetHighestRowAndBelow(string partitionKey, string rowKeyFrom, int amount) => Task.FromResult(default(IEnumerable<T>));
        public Task CleanAndKeepMaxPartitions(int maxAmount) => Task.CompletedTask;
        public Task CleanAndKeepMaxRecords(string partitionKey, int maxAmount) => Task.CompletedTask;
        public Task<int> GetCountAsync(string partitionKey) => Task.FromResult(0);
    }
}
