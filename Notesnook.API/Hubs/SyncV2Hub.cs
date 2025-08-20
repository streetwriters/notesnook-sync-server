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
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using Notesnook.API.Authorization;
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
        Task<bool> SendMonographs(IEnumerable<Monograph> monographs);
        Task PushCompleted();
    }

    [Authorize("Sync")]
    public class SyncV2Hub : Hub<ISyncV2HubClient>
    {
        private ISyncItemsRepositoryAccessor Repositories { get; }
        private readonly IUnitOfWork unit;
        private readonly string[] CollectionKeys = [
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

        public SyncV2Hub(ISyncItemsRepositoryAccessor syncItemsRepositoryAccessor, IUnitOfWork unitOfWork)
        {
            Repositories = syncItemsRepositoryAccessor;
            unit = unitOfWork;
        }

        public override async Task OnConnectedAsync()
        {
            var result = new SyncRequirement().IsAuthorized(Context.User, new PathString("/hubs/sync/v2"));
            if (!result.Succeeded)
            {
                var reason = result.AuthorizationFailure.FailureReasons.FirstOrDefault();
                throw new HubException(reason?.Message ?? "Unauthorized");
            }
            var id = Context.User.FindFirstValue("sub");
            await Groups.AddToGroupAsync(Context.ConnectionId, id);
            await base.OnConnectedAsync();
        }

        private Action<IEnumerable<SyncItem>, string, long> MapTypeToUpsertAction(string type)
        {
            return type switch
            {
                "settingitem" => Repositories.Settings.UpsertMany,
                "attachment" => Repositories.Attachments.UpsertMany,
                "note" => Repositories.Notes.UpsertMany,
                "notebook" => Repositories.Notebooks.UpsertMany,
                "content" => Repositories.Contents.UpsertMany,
                "shortcut" => Repositories.Shortcuts.UpsertMany,
                "reminder" => Repositories.Reminders.UpsertMany,
                "relation" => Repositories.Relations.UpsertMany,
                "color" => Repositories.Colors.UpsertMany,
                "vault" => Repositories.Vaults.UpsertMany,
                "tag" => Repositories.Tags.UpsertMany,
                _ => null,
            };
        }

        public async Task<int> PushItems(string deviceId, SyncTransferItemV2 pushItem)
        {
            var userId = Context.User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) throw new HubException("Please login to sync.");

            SyncEventCounterSource.Log.PushV2();

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {

                var UpsertItems = MapTypeToUpsertAction(pushItem.Type) ?? throw new Exception($"Invalid item type: {pushItem.Type}.");
                UpsertItems(pushItem.Items, userId, 1);

                if (!await unit.Commit()) return 0;

                new SyncDeviceService(new SyncDevice(userId, deviceId)).AddIdsToOtherDevices(pushItem.Items.Select((i) => $"{i.ItemId}:{pushItem.Type}").ToList());
                return 1;
            }
            finally
            {
                stopwatch.Stop();
                SyncEventCounterSource.Log.RecordPushDuration(stopwatch.ElapsedMilliseconds);
            }
        }

        public async Task<bool> PushCompleted()
        {
            var userId = Context.User.FindFirstValue("sub");
            await Clients.OthersInGroup(userId).PushCompleted();
            return true;
        }

        private static async IAsyncEnumerable<SyncTransferItemV2> PrepareChunks(Func<string, string[], bool, int, Task<IAsyncCursor<SyncItem>>>[] collections, string[] types, string userId, string[] ids, int size, bool resetSync, long maxBytes)
        {
            var itemsProcessed = 0;
            for (int i = 0; i < collections.Length; i++)
            {
                var type = types[i];

                var filteredIds = ids.Where((id) => id.EndsWith($":{type}")).Select((id) => id.Split(":")[0]).ToArray();
                if (!resetSync && filteredIds.Length == 0) continue;

                using var cursor = await collections[i](userId, filteredIds, resetSync, size);

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
            return await HandleRequestFetch(deviceId, false);
        }

        public async Task<SyncV2Metadata> RequestFetchV2(string deviceId)
        {
            return await HandleRequestFetch(deviceId, true);
        }

        private async Task<SyncV2Metadata> HandleRequestFetch(string deviceId, bool includeMonographs)
        {
            var userId = Context.User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) throw new HubException("Please login to sync.");

            SyncEventCounterSource.Log.FetchV2();

            var device = new SyncDevice(userId, deviceId);
            var deviceService = new SyncDeviceService(device);
            if (!deviceService.IsDeviceRegistered()) deviceService.RegisterDevice();

            device.LastAccessTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var isResetSync = deviceService.IsSyncReset();
            if (!deviceService.IsUnsynced() &&
                !deviceService.IsSyncPending() &&
                !isResetSync)
                return new SyncV2Metadata { Synced = true };

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                string[] ids = deviceService.FetchUnsyncedIds();

                var chunks = PrepareChunks(
                    collections: [
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
                    ],
                    types: CollectionKeys,
                    userId,
                    ids,
                    size: 1000,
                    resetSync: isResetSync,
                    maxBytes: 7 * 1024 * 1024
                );

                var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId.Equals(userId));
                if (userSettings.VaultKey != null)
                {
                    if (!await Clients.Caller.SendVaultKey(userSettings.VaultKey).WaitAsync(TimeSpan.FromMinutes(10))) throw new HubException("Client rejected vault key.");
                }


                await foreach (var chunk in chunks)
                {
                    if (!await Clients.Caller.SendItems(chunk).WaitAsync(TimeSpan.FromMinutes(10))) throw new HubException("Client rejected sent items.");

                    if (!isResetSync)
                    {
                        var syncedIds = chunk.Items.Select((i) => $"{i.ItemId}:{chunk.Type}").ToHashSet();
                        ids = ids.Where((id) => !syncedIds.Contains(id)).ToArray();
                        deviceService.WritePendingIds(ids);
                    }
                }

                if (includeMonographs)
                {
                    var unsyncedMonographs = ids.Where((id) => id.EndsWith(":monograph")).ToHashSet();
                    var unsyncedMonographIds = unsyncedMonographs.Select((id) => id.Split(":")[0]).ToArray();
                    var userMonographs = isResetSync
                        ? await Repositories.Monographs.FindAsync(m => m.UserId == userId)
                        : await Repositories.Monographs.FindAsync(m => m.UserId == userId && unsyncedMonographIds.Contains(m.ItemId));

                    if (userMonographs.Any() && !await Clients.Caller.SendMonographs(userMonographs).WaitAsync(TimeSpan.FromMinutes(10)))
                        throw new HubException("Client rejected monographs.");
                }

                deviceService.Reset();

                return new SyncV2Metadata
                {
                    Synced = true,
                };
            }
            finally
            {
                stopwatch.Stop();
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
    public struct SyncV2TransferItem
    {
        [MessagePack.Key("items")]
        [JsonPropertyName("items")]
        public IEnumerable<SyncItem> Items { get; set; }

        [MessagePack.Key("type")]
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [MessagePack.Key("final")]
        [JsonPropertyName("final")]
        public bool Final { get; set; }

        [MessagePack.Key("vaultKey")]
        [JsonPropertyName("vaultKey")]
        public EncryptedData VaultKey { get; set; }
    }
}