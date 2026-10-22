using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Services;

namespace Notesnook.API.Tests;

public class MongoIndexesTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private readonly MongoIndexesHostedService service = new(db.Client, NullLogger<MongoIndexesHostedService>.Instance);
    private IMongoDatabase Database => db.Client.GetDatabase(db.Name);

    private static async Task<BsonDocument?> IndexAsync(IMongoDatabase database, string collection, string name)
    {
        var indexes = await (await database.GetCollection<BsonDocument>(collection).Indexes.ListAsync()).ToListAsync();
        return indexes.FirstOrDefault((index) => index["name"] == name);
    }

    [Fact]
    public async Task Creates_unique_sync_indexes_and_is_idempotent()
    {
        await service.EnsureIndexesAsync(Database, CancellationToken.None);
        await service.EnsureIndexesAsync(Database, CancellationToken.None);

        var index = await IndexAsync(Database, "content", "UserId_1_ItemId_1");
        Assert.NotNull(index);
        Assert.True(index["unique"].ToBoolean());
        Assert.NotNull(await IndexAsync(Database, "sync_devices", "UserId_1_DeviceId_1"));
    }

    [Fact]
    public async Task Existing_duplicates_dont_stop_the_other_indexes()
    {
        var database = db.Client.GetDatabase(db.Name + "_duplicates");
        try
        {
            await database.GetCollection<BsonDocument>("tags").InsertManyAsync(
                [new BsonDocument { ["UserId"] = "u", ["ItemId"] = "i" }, new BsonDocument { ["UserId"] = "u", ["ItemId"] = "i" }]);

            await service.EnsureIndexesAsync(database, CancellationToken.None);

            Assert.Null(await IndexAsync(database, "tags", "UserId_1_ItemId_1"));
            Assert.NotNull(await IndexAsync(database, "tags", "UserId_1__id_1"));
            Assert.NotNull(await IndexAsync(database, "vaults", "UserId_1_ItemId_1"));
        }
        finally
        {
            await db.Client.DropDatabaseAsync(database.DatabaseNamespace.DatabaseName);
        }
    }

    [Fact]
    public async Task An_unreachable_database_doesnt_stop_the_host()
    {
        var unreachable = new MongoClient(new MongoClientSettings
        {
            Server = new MongoServerAddress("127.0.0.1", 1),
            ServerSelectionTimeout = TimeSpan.FromMilliseconds(200)
        });
        using var hosted = new MongoIndexesHostedService(unreachable, NullLogger<MongoIndexesHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        await hosted.ExecuteTask!;

        Assert.True(hosted.ExecuteTask.IsCompletedSuccessfully);
    }
}
