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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Notesnook.API.Authorization;
using Notesnook.API.Extensions;
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
        private static readonly string[] CollectionKeys = [
            "settingitem",
            "attachment",
            "note",
            "notebook",
            "content",
            "shortcut",
            "reminder",
            "color",
            "tag",
            "vault",
            "relation", // relations must sync at the end to prevent invalid state
        ];
        private readonly FrozenDictionary<string, Action<IEnumerable<SyncItem>, string, long>> UpsertActionsMap;
        private readonly Func<string, IEnumerable<string>, bool, int, Task<IAsyncCursor<SyncItem>>>[] Collections;
        ILogger<SyncV2Hub> Logger { get; }

        public SyncV2Hub(ISyncItemsRepositoryAccessor syncItemsRepositoryAccessor, IUnitOfWork unitOfWork, SyncDeviceService syncDeviceService, ILogger<SyncV2Hub> logger)
        {
            Logger = logger;
            Repositories = syncItemsRepositoryAccessor;
            unit = unitOfWork;
            SyncDeviceService = syncDeviceService;

            Collections = [
                Repositories.Settings.FindItemsById,
                Repositories.Attachments.FindItemsById,
                Repositories.Notes.FindItemsById,
                Repositories.Notebooks.FindItemsById,
                Repositories.Contents.FindItemsById,
                Repositories.Shortcuts.FindItemsById,
                Repositories.Reminders.FindItemsById,
                Repositories.Colors.FindItemsById,
                Repositories.Tags.FindItemsById,
                Repositories.Vaults.FindItemsById,
                Repositories.Relations.FindItemsById,
            ];
            UpsertActionsMap = new Dictionary<string, Action<IEnumerable<SyncItem>, string, long>> {
                { "settingitem", Repositories.Settings.UpsertMany },
                { "attachment", Repositories.Attachments.UpsertMany },
                { "note", Repositories.Notes.UpsertMany },
                { "notebook", Repositories.Notebooks.UpsertMany },
                { "content", Repositories.Contents.UpsertMany },
                { "shortcut", Repositories.Shortcuts.UpsertMany },
                { "reminder", Repositories.Reminders.UpsertMany },
                { "relation", Repositories.Relations.UpsertMany },
                { "color", Repositories.Colors.UpsertMany },
                { "vault", Repositories.Vaults.UpsertMany },
                { "tag", Repositories.Tags.UpsertMany },
            }.ToFrozenDictionary();
        }

        public override async Task OnConnectedAsync()
        {
            var result = new SyncRequirement().IsAuthorized(Context.User, new PathString("/hubs/sync/v2"));
            if (!result.Succeeded)
            {
                var reason = result.AuthorizationFailure?.FailureReasons.FirstOrDefault();
                throw new HubException(reason?.Message ?? "Unauthorized");
            }
            var id = Context.User?.FindFirstValue("sub") ?? throw new HubException("User not found.");
            await Groups.AddToGroupAsync(Context.ConnectionId, id);
            await base.OnConnectedAsync();
        }


        public async Task<int> PushItems(string deviceId, SyncTransferItemV2 pushItem)
        {
            var userId = Context.User?.FindFirstValue("sub") ?? throw new HubException("Please login to sync.");

            SyncEventCounterSource.Log.PushV2();

            var stopwatch = Stopwatch.StartNew();
            try
            {

                var UpsertItems = UpsertActionsMap[pushItem.Type] ?? throw new Exception($"Invalid item type: {pushItem.Type}.");
                UpsertItems(pushItem.Items, userId, 1);

                if (!await unit.Commit()) return 0;

                await SyncDeviceService.AddIdsToOtherDevicesAsync(userId, deviceId, pushItem.Items.Select((i) => new ItemKey(i.ItemId, pushItem.Type)));
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

        private async IAsyncEnumerable<SyncTransferItemV2> PrepareChunks(string userId, HashSet<ItemKey> ids, int size, bool resetSync, long maxBytes)
        {
            var itemsProcessed = 0;
            for (int i = 0; i < Collections.Length; i++)
            {
                var type = CollectionKeys[i];

                var filteredIds = ids.Where((id) => id.Type == type).Select((id) => id.ItemId).ToArray();
                if (!resetSync && filteredIds.Length == 0) continue;

                using var cursor = await Collections[i](userId, filteredIds, resetSync, size);

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
            return await HandleRequestFetch(deviceId, false, false);
        }

        public async Task<SyncV2Metadata> RequestFetchV2(string deviceId)
        {
            return await HandleRequestFetch(deviceId, true, false);
        }

        public async Task<SyncV2Metadata> RequestFetchV3(string deviceId)
        {
            return await HandleRequestFetch(deviceId, true, true);
        }

        private async Task<SyncV2Metadata> HandleRequestFetch(string deviceId, bool includeMonographs, bool includeInboxItems)
        {
            var userId = Context.User?.FindFirstValue("sub") ?? throw new HubException("Please login to sync.");

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
                    maxBytes: 7 * 1024 * 1024
                );

                await foreach (var chunk in chunks)
                {
                    if (!await Clients.Caller.SendItems(chunk).WaitAsync(TimeSpan.FromMinutes(10))) throw new HubException("Client rejected sent items.");

                    if (!device.IsSyncReset)
                    {
                        ids.ExceptWith(chunk.Items.Select(i => new ItemKey(i.ItemId, chunk.Type)));
                        await SyncDeviceService.WritePendingIdsAsync(userId, deviceId, ids);
                    }
                }

                if (includeMonographs)
                {
                    var unsyncedMonographIds = ids.Where(k => k.Type == "monograph").Select(k => k.ItemId);
                    FilterDefinition<Monograph> filter = device.IsSyncReset
                        ? Builders<Monograph>.Filter.Eq(m => m.UserId, userId)
                        : Builders<Monograph>.Filter.And(
                            Builders<Monograph>.Filter.Eq(m => m.UserId, userId),
                            Builders<Monograph>.Filter.Or(
                                Builders<Monograph>.Filter.In(m => m.ItemId, unsyncedMonographIds),
                                Builders<Monograph>.Filter.In("_id", unsyncedMonographIds)
                            )
                        );
                    var userMonographs = await Repositories.Monographs.Collection.Find(filter).ToListAsync();
                    var userMonographMetadatas = userMonographs.Select((m) => new MonographMetadata
                    {
                        DatePublished = m.DatePublished,
                        Deleted = m.Deleted,
                        Password = m.Password,
                        SelfDestruct = m.SelfDestruct,
                        Title = m.Title,
                        ItemId = m.ItemId ?? m.Id.ToString(),
                        PublishUrl = m.ConstructPublishUrl()
                    }).ToList();

                    if (userMonographMetadatas.Count > 0 && !await Clients.Caller.SendMonographs(userMonographMetadatas).WaitAsync(TimeSpan.FromMinutes(10)))
                        throw new HubException("Client rejected monographs.");
                }

                if (includeInboxItems)
                {
                    var unsyncedInboxItemIds = ids.Where(k => k.Type == "inbox_item").Select(k => k.ItemId);
                    var userInboxItems = device.IsSyncReset
                        ? await Repositories.InboxItems.FindAsync(m => m.UserId == userId)
                        : await Repositories.InboxItems.FindAsync(m => m.UserId == userId && unsyncedInboxItemIds.Contains(m.ItemId ?? m.Id.ToString()));
                    if (userInboxItems.Any() && !await Clients.Caller.SendInboxItems(userInboxItems).WaitAsync(TimeSpan.FromMinutes(10)))
                    {
                        throw new HubException("Client rejected inbox items.");
                    }
                }

                await SyncDeviceService.ResetAsync(userId, deviceId);

                return new SyncV2Metadata
                {
                    Synced = true,
                };
            }
            finally
            {
                SyncEventCounterSource.Log.RecordFetchDuration(stopwatch.ElapsedMilliseconds);
            }
        }
    }

    [MessagePack.MessagePackObject]
    public struct SyncV2Metadata
    {
        [MessagePack.Key("synced")]
        [JsonPropertyName("synced")]
        public bool Synced { get; set; }
    }

    [MessagePack.MessagePackObject]
    public struct SyncTransferItemV2
    {
        [MessagePack.Key("items")]
        [JsonPropertyName("items")]
        public IEnumerable<SyncItem> Items { get; set; }

        [MessagePack.Key("type")]
        [JsonPropertyName("type")]
        public string Type { get; set; }
        [MessagePack.Key("count")]
        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
}