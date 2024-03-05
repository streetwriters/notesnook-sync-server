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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using Notesnook.API.Authorization;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Repositories;
using Streetwriters.Common.Models;
using Streetwriters.Data.Interfaces;

namespace Notesnook.API.Hubs
{
    public struct RunningPush
    {
        public long Timestamp { get; set; }
        public long Validity { get; set; }
        public string ConnectionId { get; set; }
    }
    public interface ISyncHubClient
    {
        Task SyncItem(SyncTransferItem transferItem);
        Task PushItems(SyncTransferItemV2 transferItem);
        Task<bool> SendItems(SyncTransferItemV2 transferItem);
        Task RemoteSyncCompleted(long lastSynced);
        Task PushCompleted(long lastSynced);
        Task SyncCompleted();
    }

    public class GlobalSync
    {
        private const long PUSH_VALIDITY_EXTENSION_PERIOD = 16 * 1000; // 16 second
        private const int PUSH_VALIDITY_PERIOD_PER_ITEM = 5 * 100; // 0.5 second
        private const long BASE_PUSH_VALIDITY_PERIOD = 5 * 1000; // 5 seconds
        private const long BASE_PUSH_VALIDITY_PERIOD_NEW = 16 * 1000; // 16 seconds
        private readonly static Dictionary<string, List<RunningPush>> PushOperations = new();

        public static void ClearPushOperations(string userId, string connectionId)
        {
            if (PushOperations.TryGetValue(userId, out List<RunningPush> operations))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var push in operations.ToArray())
                    if (push.ConnectionId == connectionId || !IsPushValid(push, now))
                        operations.Remove(push);
            }
        }

        public static bool IsPushing(string userId, string connectionId)
        {
            if (PushOperations.TryGetValue(userId, out List<RunningPush> operations))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var push in operations)
                    if (push.ConnectionId == connectionId && IsPushValid(push, now)) return true;
            }
            return false;
        }


        public static bool IsUserPushing(string userId)
        {
            var count = 0;
            if (PushOperations.TryGetValue(userId, out List<RunningPush> operations))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var push in operations)
                    if (IsPushValid(push, now)) ++count;
            }
            return count > 0;
        }

        public static void StartPush(string userId, string connectionId, long? totalItems = null)
        {
            if (IsPushing(userId, connectionId)) return;

            if (!PushOperations.ContainsKey(userId))
                PushOperations[userId] = new List<RunningPush>();

            PushOperations[userId].Add(new RunningPush
            {
                ConnectionId = connectionId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Validity = totalItems.HasValue ? BASE_PUSH_VALIDITY_PERIOD + (totalItems.Value * PUSH_VALIDITY_PERIOD_PER_ITEM) : BASE_PUSH_VALIDITY_PERIOD_NEW
            });
        }
        public static void ExtendPush(string userId, string connectionId)
        {
            if (!IsPushing(userId, connectionId) || !PushOperations.ContainsKey(userId))
            {
                StartPush(userId, connectionId);
                return;
            }

            var index = PushOperations[userId].FindIndex((push) => push.ConnectionId == connectionId);
            if (index < 0)
            {
                StartPush(userId, connectionId);
                return;
            }

            var pushOperation = PushOperations[userId][index];
            pushOperation.Validity += PUSH_VALIDITY_EXTENSION_PERIOD;
        }
        private static bool IsPushValid(RunningPush push, long now)
        {
            return now < push.Timestamp + push.Validity;
        }
    }

    [Authorize("Sync")]
    public class SyncHub : Hub<ISyncHubClient>
    {
        private ISyncItemsRepositoryAccessor Repositories { get; }
        private readonly IUnitOfWork unit;
        private readonly string[] CollectionKeys = new[] {
            "settings",
            "attachment",
            "note",
            "notebook",
            "content",
            "shortcut",
            "reminder",
            "color",
            "tag",
            "relation", // relations must sync at the end to prevent invalid state
        };

        public SyncHub(ISyncItemsRepositoryAccessor syncItemsRepositoryAccessor, IUnitOfWork unitOfWork)
        {
            Repositories = syncItemsRepositoryAccessor;
            unit = unitOfWork;
        }

        public override async Task OnConnectedAsync()
        {
            var result = new SyncRequirement().IsAuthorized(Context.User, new PathString("/hubs/sync"));
            if (!result.Succeeded)
            {
                var reason = result.AuthorizationFailure.FailureReasons.FirstOrDefault();
                throw new HubException(reason?.Message ?? "Unauthorized");
            }
            var id = Context.User.FindFirstValue("sub");
            await Groups.AddToGroupAsync(Context.ConnectionId, id);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            try
            {
                await base.OnDisconnectedAsync(exception);
            }
            finally
            {
                var id = Context.User.FindFirstValue("sub");
                GlobalSync.ClearPushOperations(id, Context.ConnectionId);
            }
        }

        public async Task<int> SyncItem(BatchedSyncTransferItem transferItem)
        {
            var userId = Context.User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return 0;

            UserSettings userSettings = await this.Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);
            long dateSynced = Math.Max(transferItem.LastSynced, userSettings.LastSynced);

            GlobalSync.StartPush(userId, Context.ConnectionId, transferItem.Total);

            try
            {
                var others = Clients.OthersInGroup(userId);
                for (int i = 0; i < transferItem.Items.Length; ++i)
                {
                    var data = transferItem.Items[i];
                    var type = transferItem.Types[i];

                    // We intentionally don't await here to speed up the sync. Fire and forget
                    // suits here because we don't really care if the item reaches the other
                    // devices.
                    others.SyncItem(
                            new SyncTransferItem
                            {
                                Item = data,
                                ItemType = type,
                                LastSynced = dateSynced,
                                Total = transferItem.Total,
                                Current = transferItem.Current + i
                            });

                    switch (type)
                    {
                        case "content":
                            Repositories.Contents.Upsert(JsonSerializer.Deserialize<Content>(data), userId, dateSynced);
                            break;
                        case "attachment":
                            Repositories.Attachments.Upsert(JsonSerializer.Deserialize<Attachment>(data), userId, dateSynced);
                            break;
                        case "note":
                            Repositories.Notes.Upsert(JsonSerializer.Deserialize<Note>(data), userId, dateSynced);
                            break;
                        case "notebook":
                            Repositories.Notebooks.Upsert(JsonSerializer.Deserialize<Notebook>(data), userId, dateSynced);
                            break;
                        case "shortcut":
                            Repositories.Shortcuts.Upsert(JsonSerializer.Deserialize<Shortcut>(data), userId, dateSynced);
                            break;
                        case "reminder":
                            Repositories.Reminders.Upsert(JsonSerializer.Deserialize<Reminder>(data), userId, dateSynced);
                            break;
                        case "relation":
                            Repositories.Relations.Upsert(JsonSerializer.Deserialize<Relation>(data), userId, dateSynced);
                            break;
                        case "color":
                            var color = JsonSerializer.Deserialize<Color>(data);
                            if (color.Version < 5.9) continue;
                            Repositories.Colors.Upsert(color, userId, dateSynced);
                            break;
                        case "tag":
                            var tag = JsonSerializer.Deserialize<Models.Tag>(data);
                            if (tag.Version < 5.9) continue;
                            Repositories.Tags.Upsert(tag, userId, dateSynced);
                            break;
                        case "settings":
                            var settings = JsonSerializer.Deserialize<Setting>(data);
                            settings.Id = MongoDB.Bson.ObjectId.Parse(userId);
                            settings.ItemId = userId;
                            Repositories.Settings.Upsert(settings, userId, dateSynced);
                            break;
                        case "vaultKey":
                            userSettings.VaultKey = JsonSerializer.Deserialize<EncryptedData>(data);
                            Repositories.UsersSettings.Upsert(userSettings, (u) => u.UserId == userId);
                            break;
                        default:
                            throw new HubException("Invalid item type.");
                    }

                }

                return await unit.Commit() ? 1 : 0;
            }
            catch (Exception ex)
            {
                GlobalSync.ClearPushOperations(userId, Context.ConnectionId);
                throw ex;
            }
        }

        private Action<SyncItem, string, long> MapTypeToUpsertAction(string type)
        {
            return type switch
            {
                "attachment" => Repositories.Attachments.Upsert,
                "note" => Repositories.Notes.Upsert,
                "notebook" => Repositories.Notebooks.Upsert,
                "content" => Repositories.Contents.Upsert,
                "shortcut" => Repositories.Shortcuts.Upsert,
                "reminder" => Repositories.Reminders.Upsert,
                "relation" => Repositories.Relations.Upsert,
                "color" => Repositories.Colors.Upsert,
                "tag" => Repositories.Tags.Upsert,
                _ => null,
            };
        }

        public async Task<long> InitializePush(SyncMetadata syncMetadata)
        {
            if (syncMetadata.LastSynced <= 0) throw new HubException("Last synced time cannot be zero or less than zero.");

            var userId = Context.User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return 0;

            UserSettings userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);
            long dateSynced = Math.Max(syncMetadata.LastSynced, userSettings.LastSynced);

            GlobalSync.StartPush(userId, Context.ConnectionId);


            if (
                (userSettings.VaultKey != null &&
                syncMetadata.VaultKey != null &&
                !userSettings.VaultKey.Equals(syncMetadata.VaultKey) &&
                !syncMetadata.VaultKey.IsEmpty()) ||
                (userSettings.VaultKey == null &&
                syncMetadata.VaultKey != null &&
                !syncMetadata.VaultKey.IsEmpty()))
            {
                userSettings.VaultKey = syncMetadata.VaultKey;
                await Repositories.UsersSettings.UpsertAsync(userSettings, (u) => u.UserId == userId);
            }

            return dateSynced;
        }

        public async Task<int> PushItems(SyncTransferItemV2 pushItem, long dateSynced)
        {
            var userId = Context.User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return 0;

            try
            {
                var others = Clients.OthersInGroup(userId);
                others.PushItems(pushItem);

                GlobalSync.ExtendPush(userId, Context.ConnectionId);

                if (pushItem.Type == "settings")
                {
                    var settings = pushItem.Items.First();
                    if (settings == null) return 0;
                    settings.Id = MongoDB.Bson.ObjectId.Parse(userId);
                    settings.ItemId = userId;
                    Repositories.Settings.Upsert(settings, userId, dateSynced);
                }
                else
                {
                    var UpsertItem = MapTypeToUpsertAction(pushItem.Type) ?? throw new Exception("Invalid item type.");
                    foreach (var item in pushItem.Items)
                    {
                        UpsertItem(item, userId, dateSynced);
                    }
                }

                return await unit.Commit() ? 1 : 0;
            }
            catch (Exception ex)
            {
                GlobalSync.ClearPushOperations(userId, Context.ConnectionId);
                throw ex;
            }
        }

        public async Task<bool> SyncCompleted(long dateSynced)
        {
            var userId = Context.User.FindFirstValue("sub");
            try
            {
                UserSettings userSettings = await this.Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);

                long lastSynced = Math.Max(dateSynced, userSettings.LastSynced);

                userSettings.LastSynced = lastSynced;

                await this.Repositories.UsersSettings.UpsertAsync(userSettings, (u) => u.UserId == userId);

                await Clients.OthersInGroup(userId).RemoteSyncCompleted(lastSynced);
                await Clients.OthersInGroup(userId).PushCompleted(lastSynced);

                return true;
            }
            finally
            {
                GlobalSync.ClearPushOperations(userId, Context.ConnectionId);
            }
        }

        private static async IAsyncEnumerable<SyncTransferItemV2> PrepareChunks(Func<string, long, int, Task<IAsyncCursor<SyncItem>>>[] collections, string[] types, string userId, long lastSyncedTimestamp, int size, long maxBytes, int skipChunks)
        {
            var chunksProcessed = 0;
            for (int i = 0; i < collections.Length; i++)
            {
                var type = types[i];

                using var cursor = await collections[i](userId, lastSyncedTimestamp, size);

                var chunk = new List<SyncItem>();
                long totalBytes = 0;
                long METADATA_BYTES = 5 * 1024;

                while (await cursor.MoveNextAsync())
                {
                    if (chunksProcessed++ < skipChunks) continue;
                    foreach (var item in cursor.Current)
                    {
                        chunk.Add(item);
                        totalBytes += item.Length + METADATA_BYTES;
                        if (totalBytes >= maxBytes)
                        {
                            yield return new SyncTransferItemV2
                            {
                                Items = chunk,
                                Type = type,
                                Count = chunksProcessed
                            };

                            totalBytes = 0;
                            chunk.Clear();
                        }
                    }
                }
                if (chunk.Count > 0)
                {
                    if (chunksProcessed++ < skipChunks) continue;
                    yield return new SyncTransferItemV2
                    {
                        Items = chunk,
                        Type = type,
                        Count = chunksProcessed
                    };
                }
            }
        }

        public Task<SyncMetadata> RequestFetch(long lastSyncedTimestamp)
        {
            return RequestResumableFetch(lastSyncedTimestamp);
        }

        public async Task<SyncMetadata> RequestResumableFetch(long lastSyncedTimestamp, int cursor = 0)
        {
            var userId = Context.User.FindFirstValue("sub");

            if (GlobalSync.IsUserPushing(userId))
            {
                throw new HubException("Cannot fetch data while another sync is in progress. Please try again later.");
            }

            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);
            if (userSettings.LastSynced > 0 && lastSyncedTimestamp > userSettings.LastSynced)
            {
                throw new HubException($"Provided timestamp value is too large. Server timestamp: {userSettings.LastSynced} Sent timestamp: {lastSyncedTimestamp}. Please run a Force Sync to fix this issue.");
            }
            // var client = Clients.Caller;

            if (lastSyncedTimestamp > 0 && userSettings.LastSynced == lastSyncedTimestamp)
            {
                return new SyncMetadata
                {
                    LastSynced = userSettings.LastSynced,
                };
            }

            var isResumable = lastSyncedTimestamp == 0;
            if (!isResumable) cursor = 0;

            var chunks = PrepareChunks(
                collections: new[] {
                    Repositories.Settings.FindItemsSyncedAfter,
                    Repositories.Attachments.FindItemsSyncedAfter,
                    Repositories.Notes.FindItemsSyncedAfter,
                    Repositories.Notebooks.FindItemsSyncedAfter,
                    Repositories.Contents.FindItemsSyncedAfter,
                    Repositories.Shortcuts.FindItemsSyncedAfter,
                    Repositories.Reminders.FindItemsSyncedAfter,
                    Repositories.Colors.FindItemsSyncedAfter,
                    Repositories.Tags.FindItemsSyncedAfter,
                    Repositories.Relations.FindItemsSyncedAfter,
                },
                types: CollectionKeys,
                userId,
                lastSyncedTimestamp,
                size: 1000,
                maxBytes: 7 * 1024 * 1024,
                skipChunks: cursor
            );

            await foreach (var chunk in chunks)
            {
                _ = await Clients.Caller.SendItems(chunk).WaitAsync(TimeSpan.FromMinutes(10));
            }

            return new SyncMetadata
            {
                VaultKey = userSettings.VaultKey,
                LastSynced = userSettings.LastSynced,
            };
        }

        public async IAsyncEnumerable<SyncTransferItem> FetchItems(long lastSyncedTimestamp, [EnumeratorCancellation]
        CancellationToken cancellationToken)
        {
            var userId = Context.User.FindFirstValue("sub");

            if (GlobalSync.IsUserPushing(userId))
            {
                throw new HubException("Cannot fetch data while another sync is in progress. Please try again later.");
            }

            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);
            if (userSettings.LastSynced > 0 && lastSyncedTimestamp > userSettings.LastSynced)
            {
                throw new HubException($"Provided timestamp value is too large. Server timestamp: {userSettings.LastSynced} Sent timestamp: {lastSyncedTimestamp}. Please run a Force Sync to fix this issue.");
            }

            if (lastSyncedTimestamp > 0 && userSettings.LastSynced == lastSyncedTimestamp)
            {
                yield return new SyncTransferItem
                {
                    Synced = true,
                    LastSynced = userSettings.LastSynced
                };
                yield break;
            }

            var total = (await Task.WhenAll(
                            Repositories.Attachments.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp),
                            Repositories.Notes.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp),
                            Repositories.Notebooks.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp),
                            Repositories.Contents.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp),
                            Repositories.Settings.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp),
                            Repositories.Shortcuts.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp),
                            Repositories.Reminders.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp),
                            Repositories.Relations.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp),
                            Repositories.Colors.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp),
                            Repositories.Tags.CountItemsSyncedAfterAsync(userId, lastSyncedTimestamp)
                        )).Sum((a) => a);

            if (total == 0)
            {
                yield return new SyncTransferItem
                {
                    Synced = true,
                    LastSynced = userSettings.LastSynced
                };
                yield break;
            }

            var collections = new[] {
                Repositories.Settings.FindItemsSyncedAfter,
                Repositories.Attachments.FindItemsSyncedAfter,
                Repositories.Notes.FindItemsSyncedAfter,
                Repositories.Notebooks.FindItemsSyncedAfter,
                Repositories.Contents.FindItemsSyncedAfter,
                Repositories.Shortcuts.FindItemsSyncedAfter,
                Repositories.Reminders.FindItemsSyncedAfter,
                Repositories.Colors.FindItemsSyncedAfter,
                Repositories.Tags.FindItemsSyncedAfter,
                Repositories.Relations.FindItemsSyncedAfter,
            };

            for (var i = 0; i < collections.Length; ++i)
            {
                var key = CollectionKeys[i];
                using var cursor = await collections[i](userId, lastSyncedTimestamp, 1000);

                while (await cursor.MoveNextAsync(cancellationToken))
                {
                    foreach (var item in cursor.Current)
                    {
                        yield return new SyncTransferItem
                        {
                            LastSynced = userSettings.LastSynced,
                            Synced = false,
                            Item = JsonSerializer.Serialize(item),
                            ItemType = key,
                            Total = (int)total,
                        };
                    }
                }
            }

            if (userSettings.VaultKey != null)
            {
                yield return new SyncTransferItem
                {
                    LastSynced = userSettings.LastSynced,
                    Synced = false,
                    Item = JsonSerializer.Serialize(userSettings.VaultKey),
                    ItemType = "vaultKey",
                    Total = (int)total,
                };
            }
        }

    }

    [MessagePack.MessagePackObject]
    public struct BatchedSyncTransferItem
    {
        [MessagePack.Key("lastSynced")]
        public long LastSynced { get; set; }

        [MessagePack.Key("items")]
        public string[] Items { get; set; }

        [MessagePack.Key("types")]
        public string[] Types { get; set; }

        [MessagePack.Key("total")]
        public int Total { get; set; }

        [MessagePack.Key("current")]
        public int Current { get; set; }
    }

    [MessagePack.MessagePackObject]
    public struct SyncTransferItem
    {
        [MessagePack.Key("synced")]
        public bool Synced { get; set; }

        [MessagePack.Key("lastSynced")]
        public long LastSynced { get; set; }

        [MessagePack.Key("item")]
        public string Item { get; set; }

        [MessagePack.Key("itemType")]
        public string ItemType { get; set; }

        [MessagePack.Key("total")]
        public int Total { get; set; }

        [MessagePack.Key("current")]
        public int Current { get; set; }
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

    [MessagePack.MessagePackObject]
    public struct SyncMetadata
    {
        [MessagePack.Key("vaultKey")]
        [JsonPropertyName("vaultKey")]
        public EncryptedData VaultKey { get; set; }

        [MessagePack.Key("lastSynced")]
        [JsonPropertyName("lastSynced")]
        public long LastSynced { get; set; }
        // [MessagePack.Key("total")]
        // public long TotalItems { get; set; }
    }
}