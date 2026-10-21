using Notesnook.API.Repositories;

namespace Notesnook.API.Tests;

public class SyncItemCollectionsTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    // sync, push, reset and account deletion all read this list, so a repository missing from it
    // would silently be neither synced nor deleted
    [Fact]
    public void Every_sync_item_repository_is_registered()
    {
        var accessor = db.CreateScope().Repositories;
        var repositories = accessor.GetType().GetProperties()
            .Where((p) => p.PropertyType == typeof(SyncItemsRepository))
            .Select((p) => p.GetValue(accessor))
            .ToHashSet();

        Assert.Equal(repositories, accessor.SyncItemCollections.Select((c) => c.Repository).ToHashSet<object?>());
    }

    [Fact]
    public void Relations_sync_last()
    {
        var accessor = db.CreateScope().Repositories;
        Assert.Equal("relation", accessor.SyncItemCollections.Last((c) => c.Type != null).Type);
    }
}
