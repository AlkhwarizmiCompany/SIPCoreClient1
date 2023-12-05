using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Driver; 

namespace SIPClient1
{
    public class Logger
    {
        private readonly IMongoCollection<LogDocument> _logCollection;

        public Logger(string connectionString, string databaseName, string collectionName)
        {
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase(databaseName);
            _logCollection = database.GetCollection<LogDocument>(collectionName);
        }
        public async Task LogAsync(string callId, LogEntry logEntry)
        {
            logEntry.Timestamp = logEntry.Timestamp == DateTime.MinValue ? DateTime.UtcNow : logEntry.Timestamp;

            var filter = Builders<LogDocument>.Filter.Eq(doc => doc.CallId, callId);
            var update = Builders<LogDocument>.Update.Push(doc => doc.Logs, logEntry);

            await _logCollection.UpdateOneAsync(filter, update);
        }

        public async Task CreateOrUpdateLogDocumentAsync(LogDocument logDocument)
        {
            var filter = Builders<LogDocument>.Filter.Eq(doc => doc.CallId, logDocument.CallId);
            var update = Builders<LogDocument>.Update
                .Set(doc => doc.From, logDocument.From)
                .Set(doc => doc.To, logDocument.To)
                .Set(doc => doc.CallUri, logDocument.CallUri)
                .SetOnInsert(doc => doc.Logs, new List<LogEntry>());

            await _logCollection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
        }


    }
}
