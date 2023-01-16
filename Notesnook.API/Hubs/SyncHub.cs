/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2022 Streetwriters (Private) Limited

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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Notesnook.API.Authorization;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Common.Models;
using Streetwriters.Data.Interfaces;

namespace Notesnook.API.Hubs
{
    public interface ISyncHubClient
    {
        Task SyncItem(SyncTransferItem transferItem);
        Task RemoteSyncCompleted(long lastSynced);
        Task SyncCompleted();
    }

    [Authorize("Sync")]
    public class SyncHub : Hub<ISyncHubClient>
    {
        private ISyncItemsRepositoryAccessor Repositories { get; }
        private readonly IUnitOfWork unit;

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
            var id = Context.User.FindFirstValue("sub");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, id);
            await base.OnDisconnectedAsync(exception);
        }

        public async Task<int> SyncItem(BatchedSyncTransferItem transferItem)
        {

            var userId = Context.User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return 0;

            var others = Clients.OthersInGroup(userId);

            UserSettings userSettings = await this.Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);

            long dateSynced = transferItem.LastSynced > userSettings.LastSynced ? transferItem.LastSynced : userSettings.LastSynced;

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

        public async Task<bool> SyncCompleted(long dateSynced)
        {
            var userId = Context.User.FindFirstValue("sub");

            UserSettings userSettings = await this.Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);

            long lastSynced = dateSynced > userSettings.LastSynced ? dateSynced : userSettings.LastSynced;

            userSettings.LastSynced = lastSynced;

            await this.Repositories.UsersSettings.UpsertAsync(userSettings, (u) => u.UserId == userId);

            await Clients.OthersInGroup(userId).RemoteSyncCompleted(lastSynced);
            return true;
        }

        public async IAsyncEnumerable<SyncTransferItem> FetchItems(long lastSyncedTimestamp, [EnumeratorCancellation]
        CancellationToken cancellationToken)
        {
            var userId = Context.User.FindFirstValue("sub");

            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId);
            if (userSettings.LastSynced > 0 && lastSyncedTimestamp > userSettings.LastSynced)
                throw new HubException($"Provided timestamp value is too large. Server timestamp: {userSettings.LastSynced} Sent timestamp: {lastSyncedTimestamp}");

            // var client = Clients.Caller;

            if (lastSyncedTimestamp > 0 && userSettings.LastSynced == lastSyncedTimestamp)
            {
                yield return new SyncTransferItem
                {
                    LastSynced = userSettings.LastSynced,
                    Synced = true
                };
                yield break;
            }


            var attachments = await Repositories.Attachments.GetItemsSyncedAfterAsync(userId, lastSyncedTimestamp);

            var notes = await Repositories.Notes.GetItemsSyncedAfterAsync(userId, lastSyncedTimestamp);

            var notebooks = await Repositories.Notebooks.GetItemsSyncedAfterAsync(userId, lastSyncedTimestamp);

            var contents = await Repositories.Contents.GetItemsSyncedAfterAsync(userId, lastSyncedTimestamp);

            var settings = await Repositories.Settings.GetItemsSyncedAfterAsync(userId, lastSyncedTimestamp);

            var shortcuts = await Repositories.Shortcuts.GetItemsSyncedAfterAsync(userId, lastSyncedTimestamp);

            var reminders = await Repositories.Reminders.GetItemsSyncedAfterAsync(userId, lastSyncedTimestamp);

            var relations = await Repositories.Relations.GetItemsSyncedAfterAsync(userId, lastSyncedTimestamp);

            var collections = new Dictionary<string, IEnumerable<object>>
            {
                ["attachment"] = attachments,
                ["note"] = notes,
                ["notebook"] = notebooks,
                ["content"] = contents,
                ["shortcut"] = shortcuts,
                ["reminder"] = reminders,
                ["relation"] = relations,
                ["settings"] = settings,
            };

            if (userSettings.VaultKey != null)
            {
                collections.Add("vaultKey", new object[] { userSettings.VaultKey });
            }

            var total = collections.Values.Sum((a) => a.Count());
            if (total == 0)
            {
                yield return new SyncTransferItem
                {
                    Synced = true,
                    LastSynced = userSettings.LastSynced
                };
                yield break;
            }

            foreach (var collection in collections)
            {
                foreach (var item in collection.Value)
                {
                    if (item == null) continue;
                    // Check the cancellation token regularly so that the server will stop producing items if the client disconnects.
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new SyncTransferItem
                    {
                        LastSynced = userSettings.LastSynced,
                        Synced = false,
                        Item = JsonSerializer.Serialize(item),
                        ItemType = collection.Key,
                        Total = total,
                    };
                }
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
}