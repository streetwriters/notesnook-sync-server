using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using Notesnook.API.Hubs;
using Notesnook.API.Models;
using Notesnook.API.Services;

namespace Notesnook.API.Tests;

public class FetchSenderTests(TestDatabase db) : IClassFixture<TestDatabase>
{
    private async Task<(string UserId, List<SyncItem> Notes)> InsertNotesAsync(int count, int cipherLength)
    {
        var userId = Guid.NewGuid().ToString("N");
        var notes = Enumerable.Range(0, count).Select((_) => new SyncItem
        {
            UserId = userId,
            ItemId = Guid.NewGuid().ToString("N"),
            Cipher = new string('a', cipherLength),
            IV = "iv",
            Length = cipherLength,
            Version = 5.9,
            Algorithm = Algorithms.Default
        }).ToList();
        await db.CreateScope().Repositories.Notes.Collection.InsertManyAsync(notes);
        return (userId, notes);
    }

    private CollectionDef[] Notes() => [new("note", db.CreateScope().Repositories.Notes)];

    private FetchSender Sender(string userId, Func<SyncTransferRawItemV2, Task<bool>> sendItems) =>
        new(userId, Notes(), Stub<ISyncV2HubClient>.Create(("SendItems", (args) => sendItems((SyncTransferRawItemV2)args[0]!))));

    private FetchSender Sender(params (string Method, Func<object?[], object?> Handler)[] handlers) =>
        new("user", Notes(), Stub<ISyncV2HubClient>.Create(handlers));

    private static async Task<List<List<ItemKey>>> SendItemsAsync(FetchSender sender, IEnumerable<ItemKey> ids, bool fullSync)
    {
        var sent = new List<List<ItemKey>>();
        await foreach (var sentIds in sender.SendItemsAsync(ids, fullSync)) sent.Add(sentIds);
        return sent;
    }

    [Fact]
    public async Task The_next_chunk_is_read_while_the_client_applies_the_current_one()
    {
        // 1MB items, so 3 per chunk
        var (userId, _) = await InsertNotesAsync(9, 1024 * 1024);
        var sent = new List<SyncTransferRawItemV2>();
        var sender = Sender(userId, async (chunk) =>
        {
            sent.Add(chunk);
            if (sent.Count > 1) return true;
            // the second chunk was read while this one was applied, so deleting its items now doesn't change it
            await Task.Delay(500);
            await db.CreateScope().Repositories.Notes.Collection.DeleteManyAsync((n) => n.UserId == userId);
            return true;
        });

        await SendItemsAsync(sender, [], fullSync: true);

        Assert.Equal([3, 3], sent.Select((c) => c.Items.Count()));
    }

    [Fact]
    public async Task A_chunk_is_not_kept_alive_while_the_client_applies_it()
    {
        var (userId, _) = await InsertNotesAsync(6, 1024 * 1024);
        var sentItems = new List<WeakReference>();
        var aliveWhileApplying = new List<bool>();
        // the ack can't capture the chunk, as a client that keeps a chunk alive would fail this too
        async Task<bool> ApplyAsync(WeakReference items)
        {
            await Task.Delay(200);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            aliveWhileApplying.Add(items.IsAlive);
            return true;
        }
        var sender = Sender(userId, (chunk) =>
        {
            var items = new WeakReference(chunk.Items);
            sentItems.Add(items);
            return ApplyAsync(items);
        });

        await SendItemsAsync(sender, [], fullSync: true);

        Assert.Equal([false, false], aliveWhileApplying);
    }

    [Fact]
    public async Task Only_the_given_ids_are_sent_and_reported_per_chunk()
    {
        var (userId, notes) = await InsertNotesAsync(8, 1024 * 1024);
        var pending = notes.Take(5).Select((n) => new ItemKey(n.ItemId!, "note")).ToHashSet();
        var sender = Sender(userId, (_) => Task.FromResult(true));

        var reported = new List<List<ItemKey>>();
        await foreach (var ids in sender.SendItemsAsync(pending, fullSync: false))
        {
            reported.Add(ids);
            pending.ExceptWith(ids);
        }

        Assert.Equal([3, 2], reported.Select((r) => r.Count));
        Assert.Equal(notes.Take(5).Select((n) => n.ItemId).Order(), reported.SelectMany((r) => r).Select((k) => k.ItemId).Order());
        Assert.Empty(pending);
    }

    [Fact]
    public async Task The_next_chunk_is_sent_before_the_ids_of_the_current_one_are_handed_back()
    {
        var (userId, notes) = await InsertNotesAsync(8, 1024 * 1024);
        var sent = 0;
        var sender = Sender(userId, (_) => { sent++; return Task.FromResult(true); });
        var sentWhenHandedBack = new List<int>();

        await foreach (var _ in sender.SendItemsAsync(notes.Select((n) => new ItemKey(n.ItemId!, "note")), fullSync: false))
            sentWhenHandedBack.Add(sent);

        Assert.Equal([2, 3, 3], sentWhenHandedBack);
    }

    [Fact]
    public async Task A_rejected_chunk_fails_without_reporting_it()
    {
        var (userId, notes) = await InsertNotesAsync(6, 1024 * 1024);
        var reported = new List<ItemKey>();
        var sender = Sender(userId, (_) => Task.FromResult(false));

        await Assert.ThrowsAsync<HubException>(async () =>
        {
            await foreach (var ids in sender.SendItemsAsync(notes.Select((n) => new ItemKey(n.ItemId!, "note")), fullSync: false)) reported.AddRange(ids);
        });

        Assert.Empty(reported);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public async Task Monographs_are_sent_only_if_there_are_any(int count, bool sent)
    {
        var calls = 0;
        var sender = Sender(("SendMonographs", (_) => { calls++; return Task.FromResult(true); }));

        await sender.SendMonographsAsync(Enumerable.Range(0, count).Select((_) => new MonographMetadata { ItemId = "monograph" }).ToList());

        Assert.Equal(sent ? 1 : 0, calls);
    }

    [Fact]
    public async Task Rejected_inbox_items_fail_the_fetch()
    {
        var sender = Sender(("SendInboxItems", (_) => Task.FromResult(false)));

        var error = await Assert.ThrowsAsync<HubException>(() => sender.SendInboxItemsAsync([new InboxSyncItem()]));

        Assert.Equal("Client rejected inbox items.", error.Message);
    }

    [Fact]
    public async Task Waiting_for_the_client_is_timed()
    {
        var (userId, _) = await InsertNotesAsync(6, 1024 * 1024);
        var sender = new FetchSender(userId, Notes(), Stub<ISyncV2HubClient>.Create(
            ("SendItems", (_) => Task.Delay(150).ContinueWith((_) => true)),
            ("SendMonographs", (_) => Task.Delay(150).ContinueWith((_) => true))));

        await SendItemsAsync(sender, [], fullSync: true);
        await sender.SendMonographsAsync([new MonographMetadata { ItemId = "monograph" }]);

        Assert.InRange(sender.ClientMilliseconds, 3 * 140, 3 * 1000);
    }
}
