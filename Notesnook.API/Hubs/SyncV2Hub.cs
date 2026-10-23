/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Notesnook.API.Helpers;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Data.Interfaces;

namespace Notesnook.API.Hubs
{
    public interface ISyncV2HubClient
    {
        Task<bool> SendItems(SyncTransferItemV2 transferItem);
        Task<bool> SendVaultKey(EncryptedData vaultKey);
        Task<bool> SendMonographs(IEnumerable<MonographMetadata> monographs);
        Task<bool> SendInboxItems(IEnumerable<InboxSyncItem> inboxItems);
        Task PushCompleted();
        Task PushCompletedV2(string deviceId);
    }

    [Authorize]
    public class SyncV2Hub : Hub<ISyncV2HubClient>
    {
        private ISyncItemsRepositoryAccessor Repositories { get; }
        private SyncDeviceService SyncDeviceService { get; }
        private readonly IUnitOfWork unit;
        private readonly FrozenDictionary<string, Action<IEnumerable<SyncItem>, string>> UpsertActionsMap;
        private readonly CollectionDef[] BaseCollectionDefs;
        private readonly CollectionDef[] V4CollectionDefs;
        ILogger<SyncV2Hub> Logger { get; }

        public SyncV2Hub(ISyncItemsRepositoryAccessor syncItemsRepositoryAccessor, IUnitOfWork unitOfWork, SyncDeviceService syncDeviceService, ILogger<SyncV2Hub> logger)
        {
            Logger = logger;
            Repositories = syncItemsRepositoryAccessor;
            unit = unitOfWork;
            SyncDeviceService = syncDeviceService;

            var synced = Repositories.SyncItemCollections.Where((c) => c.Type != null).ToArray();
            BaseCollectionDefs = [.. synced.Where((c) => !c.RequiresV4).Select((c) => new CollectionDef(c.Type!, c.Repository.FindItemsById))];
            V4CollectionDefs = [.. synced.Select((c) => new CollectionDef(c.Type!, c.Repository.FindItemsById))];
            UpsertActionsMap = synced.ToFrozenDictionary((c) => c.Type!, (c) => (Action<IEnumerable<SyncItem>, string>)c.Repository.UpsertMany);
        }

        public override async Task OnConnectedAsync()
        {
            var id = Context.User?.FindFirstValue("sub") ?? throw new HubException("User not found.");
            await Groups.AddToGroupAsync(Context.ConnectionId, id);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                Logger.LogWarning(exception, "Connection {ConnectionId} disconnected with error (server-side drop)", Context.ConnectionId);
            }
            else
            {
                Logger.LogInformation("Connection {ConnectionId} disconnected cleanly (client-initiated)", Context.ConnectionId);
            }
            await base.OnDisconnectedAsync(exception);
        }


        public async Task<int> PushItems(string deviceId, SyncTransferItemV2 pushItem)
        {
            var userId = Context.User?.FindFirstValue("sub") ?? throw new HubException("Please login to sync.");
            if (string.IsNullOrEmpty(deviceId)) throw new HubException("Missing device ID.");

            SyncEventCounterSource.Log.PushV2();

            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (!UpsertActionsMap.TryGetValue(pushItem.Type, out var UpsertItems))
                    throw new HubException($"Invalid item type: {pushItem.Type}.");
                UpsertItems(pushItem.Items, userId);
                if (pushItem.Items.Any())
                {
                    SyncDeviceService.QueueAddIdsToOtherDevices(userId, deviceId, pushItem.Items.Select((i) => new ItemKey(i.ItemId, pushItem.Type)));

                    // we need to delete the inbox items from the inbox collection
                    // after syncing to prevent them from being sent again in the 
                    // next fetch. inbox items are always notes.
                    if (pushItem.Type == "note")
                    {
                        var itemIds = pushItem.Items.Select(i => i.ItemId).ToList();
                        Repositories.InboxItems.DeleteMany(i => i.UserId == userId && itemIds.Contains(i.ItemId));
                    }
                }

                if (!await unit.Commit()) return 0;

                // the push is already committed, so failing to record the access time must not fail it
                try
                {
                    await SyncDeviceService.UpdateLastAccessTimeAsync(userId, deviceId);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to update last access time of device {DeviceId}", deviceId);
                }

                return 1;
            }
            finally
            {
                SyncEventCounterSource.Log.RecordPushDuration(stopwatch.ElapsedMilliseconds);
            }
        }

        public async Task<bool> PushCompleted()
        {
            var userId = Context.User?.FindFirstValue("sub") ?? throw new HubException("User not found.");
            await Clients.OthersInGroup(userId).PushCompleted();
            return true;
        }

        public async Task<bool> PushCompletedV2(string deviceId)
        {
            var userId = Context.User?.FindFirstValue("sub") ?? throw new HubException("User not found.");
            await Clients.OthersInGroup(userId).PushCompleted();
            await Clients.OthersInGroup(userId).PushCompletedV2(deviceId);
            return true;
        }

        private async IAsyncEnumerable<SyncTransferItemV2> PrepareChunks(string userId, HashSet<ItemKey> ids, int size, bool resetSync, long maxBytes, CollectionDef[] collectionDefs)
        {
            var itemsProcessed = 0;
            foreach (var def in collectionDefs)
            {
                var type = def.Key;

                var filteredIds = ids.Where((id) => id.Type == type).Select((id) => id.ItemId).ToArray();
                if (!resetSync && filteredIds.Length == 0) continue;

                using var cursor = await def.FindItems(userId, filteredIds, resetSync, size);

                var chunk = new List<SyncItem>();
                long totalBytes = 0;
                long METADATA_BYTES = 5 * 1024;

                while (await cursor.MoveNextAsync())
                {
                    foreach (var item in cursor.Current)
                    {
                        chunk.Add(item);
                        totalBytes += item.Length + METADATA_BYTES;
                        if (totalBytes >= maxBytes)
                        {
                            itemsProcessed += chunk.Count;
                            yield return new SyncTransferItemV2
                            {
                                Items = chunk,
                                Type = type,
                                Count = itemsProcessed
                            };

                            totalBytes = 0;
                            chunk.Clear();
                        }
                    }
                }
                if (chunk.Count > 0)
                {
                    itemsProcessed += chunk.Count;
                    yield return new SyncTransferItemV2
                    {
                        Items = chunk,
                        Type = type,
                        Count = itemsProcessed
                    };
                }
            }
        }

        public async Task<SyncV2Metadata> RequestFetch(string deviceId)
        {
            return await HandleRequestFetch(deviceId, false, false, BaseCollectionDefs);
        }

        public async Task<SyncV2Metadata> RequestFetchV2(string deviceId)
        {
            return await HandleRequestFetch(deviceId, true, false, BaseCollectionDefs);
        }

        public async Task<SyncV2Metadata> RequestFetchV3(string deviceId)
        {
            return await HandleRequestFetch(deviceId, true, true, BaseCollectionDefs);
        }

        public async Task<SyncV2Metadata> RequestFetchV4(string deviceId)
        {
            return await HandleRequestFetch(deviceId, true, true, V4CollectionDefs);
        }

        private async Task<SyncV2Metadata> HandleRequestFetch(string deviceId, bool includeMonographs, bool includeInboxItems, CollectionDef[] collectionDefs)
        {
            var userId = Context.User?.FindFirstValue("sub") ?? throw new HubException("Please login to sync.");
            if (string.IsNullOrEmpty(deviceId)) throw new HubException("Missing device ID.");

            SyncEventCounterSource.Log.FetchV2();

            var device = await SyncDeviceService.GetDeviceAsync(userId, deviceId);
            if (device == null)
                device = await SyncDeviceService.RegisterDeviceAsync(userId, deviceId);
            else
                await SyncDeviceService.UpdateLastAccessTimeAsync(userId, deviceId);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var ids = await SyncDeviceService.FetchUnsyncedIdsAsync(userId, deviceId);
                if (!device.IsSyncReset && ids.Count == 0)
                    return new SyncV2Metadata { Synced = true };

                var chunks = PrepareChunks(
                    userId,
                    ids,
                    size: 100,
                    resetSync: device.IsSyncReset,
                    maxBytes: 3 * 1024 * 1024,
                    collectionDefs
                );

                await foreach (var chunk in chunks)
                {
                    if (!await Clients.Caller.SendItems(chunk).WaitAsync(TimeSpan.FromMinutes(10))) throw new HubException("Client rejected sent items.");

                    if (!device.IsSyncReset)
                    {
                        var sentIds = chunk.Items.Select(i => new ItemKey(i.ItemId, chunk.Type)).ToList();
                        ids.ExceptWith(sentIds);
                        await SyncDeviceService.RemovePendingIdsAsync(userId, deviceId, sentIds);
                    }
                }

                if (includeMonographs)
                {
                    var unsyncedMonographIds = ids.Where(k => k.Type == "monograph").Select(k => k.ItemId).ToList();
                    // _id is stored as an ObjectId, so string IDs would never match it
                    var unsyncedMonographObjectIds = unsyncedMonographIds.Where(id => ObjectId.TryParse(id, out _)).Select(ObjectId.Parse);
                    FilterDefinition<Monograph> filter = device.IsSyncReset
                        ? Builders<Monograph>.Filter.Eq(m => m.UserId, userId)
                        : Builders<Monograph>.Filter.And(
                            Builders<Monograph>.Filter.Eq(m => m.UserId, userId),
                            Builders<Monograph>.Filter.Or(
                                Builders<Monograph>.Filter.In(m => m.ItemId, unsyncedMonographIds),
                                Builders<Monograph>.Filter.In("_id", unsyncedMonographObjectIds)
                            )
                        );
                    var userMonographs = await Repositories.Monographs.Collection
                        .Find(filter)
                        .Project((m) => new MonographMetadata
                        {
                            DatePublished = m.DatePublished,
                            Deleted = m.Deleted,
                            Password = m.Password,
                            SelfDestruct = m.SelfDestruct,
                            Title = m.Title,
                            ItemId = m.ItemId ?? m.Id.ToString(),
                            PublishUrl = m.Slug // this will be converted to full url in the end, but we only need slug for now
                        })
                        .ToListAsync();

                    userMonographs = userMonographs.Select((p) =>
                    {
                        p.PublishUrl = UrlHelper.ConstructPublishUrl(p);
                        return p;
                    }).ToList();

                    if (userMonographs.Count > 0 && !await Clients.Caller.SendMonographs(userMonographs).WaitAsync(TimeSpan.FromMinutes(10)))
                        throw new HubException("Client rejected monographs.");
                }

                if (includeInboxItems)
                {
                    var unsyncedInboxItemIds = ids.Where(k => k.Type == "inbox_item").Select(k => k.ItemId);
                    var userInboxItems = device.IsSyncReset
                        ? await Repositories.InboxItems.FindAsync(m => m.UserId == userId)
                        : await Repositories.InboxItems.FindAsync(m => m.UserId == userId && unsyncedInboxItemIds.Contains(m.ItemId));
                    if (userInboxItems.Any() && !await Clients.Caller.SendInboxItems(userInboxItems).WaitAsync(TimeSpan.FromMinutes(10)))
                    {
                        throw new HubException("Client rejected inbox items.");
                    }
                }

                // a device that fetched across a reset re-registered after its deletion, and must still do a full sync after logging in again
                if (await Repositories.Generations.IsCurrentAsync(userId))
                    await SyncDeviceService.ResetAsync(userId, deviceId);

                return new SyncV2Metadata
                {
                    Synced = true,
                };
            }
            catch
            {
                SyncEventCounterSource.Log.FetchV2Failed();
                throw;
            }
            finally
            {
                SyncEventCounterSource.Log.RecordFetchDuration(stopwatch.ElapsedMilliseconds);
            }
        }

        private record CollectionDef(
            string Key,
            Func<string, IEnumerable<string>, bool, int, Task<IAsyncCursor<SyncItem>>> FindItems
        );
    }

    public struct SyncV2Metadata
    {
        [JsonPropertyName("synced")]
        public bool Synced { get; set; }
    }

    public struct SyncTransferItemV2
    {
        [JsonPropertyName("items")]
        public IEnumerable<SyncItem> Items { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
}