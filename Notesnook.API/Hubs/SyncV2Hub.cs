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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using Notesnook.API.Repositories;
using Notesnook.API.Services;
using Streetwriters.Data.Interfaces;

namespace Notesnook.API.Hubs
{
    public interface ISyncV2HubClient
    {
        Task<bool> SendItems(SyncTransferRawItemV2 transferItem);
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
        ILogger<SyncV2Hub> Logger { get; }

        public SyncV2Hub(ISyncItemsRepositoryAccessor syncItemsRepositoryAccessor, IUnitOfWork unitOfWork, SyncDeviceService syncDeviceService, ILogger<SyncV2Hub> logger)
        {
            Logger = logger;
            Repositories = syncItemsRepositoryAccessor;
            unit = unitOfWork;
            SyncDeviceService = syncDeviceService;
        }

        // a hub is created for every invocation, so the collections are looked up rather than mapped up front
        private SyncItemsRepository? FindRepository(string type)
        {
            foreach (var collection in Repositories.SyncItemCollections)
                if (collection.Type == type) return collection.Repository;
            return null;
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


        public async Task<int> PushItems(string deviceId, SyncTransferPushItemV2 pushItem)
        {
            // the unit of work is done with the ciphers by the time this returns, so their pooled buffers can go back
            using var _ = pushItem;
            var userId = Context.User?.FindFirstValue("sub") ?? throw new HubException("Please login to sync.");
            if (string.IsNullOrEmpty(deviceId)) throw new HubException("Missing device ID.");

            SyncEventCounterSource.Log.PushV2();

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var repository = FindRepository(pushItem.Type) ?? throw new HubException($"Invalid item type: {pushItem.Type}.");
                repository.UpsertMany(pushItem.Items, userId);
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

        public async Task<SyncV2Metadata> RequestFetch(string deviceId)
        {
            return await HandleRequestFetch(deviceId, false, false, CollectionDefs(includeV4: false));
        }

        public async Task<SyncV2Metadata> RequestFetchV2(string deviceId)
        {
            return await HandleRequestFetch(deviceId, true, false, CollectionDefs(includeV4: false));
        }

        public async Task<SyncV2Metadata> RequestFetchV3(string deviceId)
        {
            return await HandleRequestFetch(deviceId, true, true, CollectionDefs(includeV4: false));
        }

        public async Task<SyncV2Metadata> RequestFetchV4(string deviceId)
        {
            return await HandleRequestFetch(deviceId, true, true, CollectionDefs(includeV4: true));
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
                await SyncDeviceService.UpdateLastAccessTimeAsync(device);

            var stopwatch = Stopwatch.StartNew();
            var sender = new FetchSender(userId, collectionDefs, Clients.Caller);
            try
            {
                var ids = await SyncDeviceService.FetchUnsyncedIdsAsync(device);
                if (!device.IsSyncReset && ids.Count == 0)
                    return new SyncV2Metadata { Synced = true };

                await foreach (var sentIds in sender.SendItemsAsync(ids, device.IsSyncReset))
                {
                    if (!device.IsSyncReset) await SyncDeviceService.RemovePendingIdsAsync(userId, deviceId, sentIds);
                }

                if (includeMonographs)
                    await sender.SendMonographsAsync(await FindMonographsAsync(userId, IdsOfType(ids, "monograph"), device.IsSyncReset));
                if (includeInboxItems)
                    await sender.SendInboxItemsAsync(await FindInboxItemsAsync(userId, IdsOfType(ids, "inbox_item"), device.IsSyncReset));

                // a device that fetched across a reset re-registered after its deletion, and must still do a full sync after logging in again
                if (await Repositories.Generations.IsCurrentAsync(userId))
                    await SyncDeviceService.ResetAsync(userId, deviceId, device.IsSyncReset);

                return new SyncV2Metadata
                {
                    Synced = true,
                };
            }
            catch (Exception ex)
            {
                SyncEventCounterSource.Log.FetchV2Failed(ex switch
                {
                    _ when Context.ConnectionAborted.IsCancellationRequested => "disconnected",
                    IOException or OperationCanceledException => "disconnected",
                    TimeoutException => "timeout",
                    // the client threw while handling what we sent, or acked it with false
                    HubException => "client_error",
                    _ => "server_error",
                });
                throw;
            }
            finally
            {
                SyncEventCounterSource.Log.RecordFetchDuration(stopwatch.ElapsedMilliseconds, sender.ClientMilliseconds, device.IsSyncReset);
            }
        }

        private static List<string> IdsOfType(IEnumerable<ItemKey> ids, string type) => [.. ids.Where((k) => k.Type == type).Select((k) => k.ItemId)];

        private async Task<List<MonographMetadata>> FindMonographsAsync(string userId, List<string> ids, bool fullSync)
        {
            if (!fullSync && ids.Count == 0) return [];
            // _id is stored as an ObjectId, so string IDs would never match it
            var objectIds = ids.Where(id => ObjectId.TryParse(id, out _)).Select(ObjectId.Parse);
            FilterDefinition<Monograph> filter = fullSync
                ? Builders<Monograph>.Filter.Eq(m => m.UserId, userId)
                : Builders<Monograph>.Filter.And(
                    Builders<Monograph>.Filter.Eq(m => m.UserId, userId),
                    Builders<Monograph>.Filter.Or(
                        Builders<Monograph>.Filter.In(m => m.ItemId, ids),
                        Builders<Monograph>.Filter.In("_id", objectIds)
                    )
                );
            var monographs = await Repositories.Monographs.Collection
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
            foreach (var monograph in monographs) monograph.PublishUrl = UrlHelper.ConstructPublishUrl(monograph);
            return monographs;
        }

        private async Task<List<InboxSyncItem>> FindInboxItemsAsync(string userId, List<string> ids, bool fullSync)
        {
            if (fullSync) return [.. await Repositories.InboxItems.FindAsync(m => m.UserId == userId)];
            if (ids.Count == 0) return [];
            return [.. await Repositories.InboxItems.FindAsync(m => m.UserId == userId && ids.Contains(m.ItemId))];
        }

        private CollectionDef[] CollectionDefs(bool includeV4) =>
            [.. Repositories.SyncItemCollections.Where((c) => c.Type != null && (includeV4 || !c.RequiresV4)).Select((c) => new CollectionDef(c.Type!, c.Repository))];

    }

    public struct SyncV2Metadata
    {
        [JsonPropertyName("synced")]
        public bool Synced { get; set; }
    }

    // SyncTransferItemV2 as clients push it, with the ciphers kept as UTF-8
    public struct SyncTransferPushItemV2 : IDisposable
    {
        [JsonPropertyName("items")]
        public List<PushedSyncItem> Items { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("count")]
        public int Count { get; set; }

        // clients never send null items, but a message can, and cleanup must not throw over the real error
        public readonly void Dispose() => Items?.ForEach((item) => item?.Dispose());
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

    /// <summary>
    /// What <see cref="SyncTransferItemV2"/> is on the wire, with items that are sent from their stored bytes.
    /// </summary>
    public struct SyncTransferRawItemV2
    {
        [JsonPropertyName("items")]
        public IEnumerable<RawSyncItem> Items { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
}