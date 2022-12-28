using System.Threading;
using System;
using System.Threading.Tasks;

namespace MongoDB.Driver
{
    public static class TransactionHelper
    {
        public static async Task StartTransaction(this IMongoClient client, Action<CancellationToken> operate, CancellationToken ct)
        {
            using (var session = await client.StartSessionAsync())
            {
                var transactionOptions = new TransactionOptions(readPreference: ReadPreference.Nearest, readConcern: ReadConcern.Local, writeConcern: WriteConcern.WMajority);
                await session.WithTransactionAsync((handle, token) =>
                {
                    return Task.Run(() =>
                    {
                        operate(token);
                        return true;
                    });
                }, transactionOptions, ct);
            }
        }
    }
}