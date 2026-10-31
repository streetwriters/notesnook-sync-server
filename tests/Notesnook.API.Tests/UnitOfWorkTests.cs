using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Models;

namespace Notesnook.API.Tests;

public class UnitOfWorkTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private static string NewUserId() => Guid.NewGuid().ToString("N");

    // #10: an empty batch (e.g. an empty push) is a successful no-op
    [Fact]
    public async Task Empty_commit_succeeds()
    {
        var scope = db.CreateScope();
        scope.Repositories.Notes.UpsertMany([], NewUserId());

        Assert.True(await scope.Unit.Commit());
    }

    // #10: committing twice must not replay the first batch
    [Fact]
    public async Task Commands_are_cleared_after_commit()
    {
        var scope = db.CreateScope();
        var device = new SyncDevice { Id = ObjectId.GenerateNewId(), UserId = NewUserId(), DeviceId = "A", LastAccessTime = 0, IsSyncReset = false };
        scope.Repositories.SyncDevices.Insert(device);

        Assert.True(await scope.Unit.Commit());
        // a replayed insert would fail with a duplicate key error
        Assert.True(await scope.Unit.Commit());
        Assert.Equal(1, await scope.Repositories.SyncDevices.Collection.CountDocumentsAsync(d => d.UserId == device.UserId));
    }

    // #10: errors reach the caller instead of being swallowed
    [Fact]
    public async Task Commit_throws_on_failure()
    {
        var scope = db.CreateScope();
        scope.DbContext.AddCommand((_, _) => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Unit.Commit());
    }

    // #10: commands share one session, so they must run one after another, in order
    [Fact]
    public async Task Commands_run_sequentially_in_order()
    {
        var userId = NewUserId();
        var scope = db.CreateScope();
        var collection = scope.Repositories.Notes.Collection;
        var running = 0;
        var overlapped = false;
        var seen = new List<long>();

        for (var i = 0; i < 20; i++)
        {
            var expectedBefore = i;
            scope.DbContext.AddCommand(async (session, ct) =>
            {
                if (Interlocked.Increment(ref running) > 1) overlapped = true;
                // every earlier command's write must already be visible in this session
                seen.Add(await collection.CountDocumentsAsync(session, Builders<SyncItem>.Filter.Eq(x => x.UserId, userId), cancellationToken: ct));
                await collection.InsertOneAsync(session, new SyncItem { UserId = userId, ItemId = $"{expectedBefore}" }, cancellationToken: ct);
                Interlocked.Decrement(ref running);
            });
        }

        Assert.True(await scope.Unit.Commit());
        Assert.False(overlapped);
        Assert.Equal(Enumerable.Range(0, 20).Select(i => (long)i), seen);
    }

    // #10: all commands commit or roll back together
    [Fact]
    public async Task Failed_commit_rolls_back_everything()
    {
        var userId = NewUserId();
        var scope = db.CreateScope();
        scope.Repositories.Notes.UpsertMany([TestItems.PushedNote("1"), TestItems.PushedNote("2")], userId);
        scope.DbContext.AddCommand((_, _) => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Unit.Commit());
        Assert.Equal(0, await scope.Repositories.Notes.Collection.CountDocumentsAsync(i => i.UserId == userId));
    }
}
