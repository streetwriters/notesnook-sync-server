using EphemeralMongo;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Notesnook.API.Accessors;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Data;
using Streetwriters.Data.DbContexts;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Tests;

/// <summary>
/// A throwaway database on an ephemeral single-node MongoDB replica set
/// (transactions don't work on a standalone server).
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    // one mongod for the whole test run, stopped when the test process exits
    private static readonly Lazy<IMongoRunner> Runner = new(() =>
    {
        var runner = MongoRunner.Run(new MongoRunnerOptions
        {
            UseSingleNodeReplicaSet = true,
            KillMongoProcessesWhenCurrentProcessExits = true
        });
        AppDomain.CurrentDomain.ProcessExit += (_, _) => runner.Dispose();
        return runner;
    });

    public IMongoClient Client { get; }
    public string Name { get; } = "notesnook_test_" + Guid.NewGuid().ToString("N");

    public TestDatabase()
    {
        Client = MongoDbContext.CreateMongoDbClient(new TestDbSettings { ConnectionString = Runner.Value.ConnectionString });
    }

    public IMongoCollection<T> Collection<T>(string name) => MongoDbContext.GetMongoCollection<T>(Client, Name, name);

    /// <summary>
    /// The services of a single request scope, wired the same way as in Startup.
    /// </summary>
    public Scope CreateScope() => new(this);

    public async Task InitializeAsync()
    {
        // collections can't be created implicitly inside a transaction on older servers
        foreach (var name in new[] { Collections.NotesKey, Collections.SyncDevicesKey, Collections.DeviceIdsChunksKey })
            await Client.GetDatabase(Name).CreateCollectionAsync(name);
    }

    public Task DisposeAsync() => Client.DropDatabaseAsync(Name);

    public sealed class Scope
    {
        public MongoDbContext DbContext { get; }
        public UnitOfWork Unit { get; }
        public SyncItemsRepositoryAccessor Repositories { get; }
        public SyncDeviceService Devices { get; }

        public Scope(TestDatabase db)
        {
            DbContext = new MongoDbContext(db.Client);
            Unit = new UnitOfWork(DbContext);
            IMongoCollection<SyncItem> items(string name) => db.Collection<SyncItem>(name);
            Repositories = new SyncItemsRepositoryAccessor(DbContext,
                items(Collections.NotebooksKey), items(Collections.NotesKey), items(Collections.ContentKey),
                items(Collections.SettingsKey), items(Collections.LegacySettingsKey), items(Collections.AttachmentsKey),
                items(Collections.ShortcutsKey), items(Collections.RemindersKey), items(Collections.RelationsKey),
                items(Collections.ColorsKey), items(Collections.VaultsKey), items(Collections.TagsKey),
                items(Collections.InboxItemsHistoryKey),
                new Repository<UserSettings>(DbContext, db.Collection<UserSettings>("user_settings")),
                new Repository<Monograph>(DbContext, db.Collection<Monograph>("monographs")),
                new Repository<InboxApiKey>(DbContext, db.Collection<InboxApiKey>(Collections.InboxApiKeysKey)),
                new Repository<InboxSyncItem>(DbContext, db.Collection<InboxSyncItem>(Collections.InboxItemsKey)),
                new Repository<SyncDevice>(DbContext, db.Collection<SyncDevice>(Collections.SyncDevicesKey)),
                new Repository<DeviceIdsChunk>(DbContext, db.Collection<DeviceIdsChunk>(Collections.DeviceIdsChunksKey)),
                NullLogger<Notesnook.API.Repositories.SyncItemsRepository>.Instance);
            Devices = new SyncDeviceService(Repositories, DbContext, NullLogger<SyncDeviceService>.Instance);
        }
    }

    private sealed class TestDbSettings : Streetwriters.Data.Interfaces.IDbSettings
    {
        public string DatabaseName { get; set; } = "";
        public string ConnectionString { get; set; } = "";
    }
}
