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
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using Notesnook.API.Models;
using Notesnook.API.Repositories;
using Notesnook.API.Services;

namespace Notesnook.API.Hubs
{
    public record CollectionDef(string Key, SyncItemsRepository Repository);

    public sealed class FetchSender(string userId, IReadOnlyList<CollectionDef> collections, ISyncV2HubClient client)
    {
        private const long MaxChunkBytes = 3 * 1024 * 1024;
        // clients apply each chunk as one batch, so small items must not pack thousands into one
        private const int MaxChunkItems = 500;
        private static readonly TimeSpan AckTimeout = TimeSpan.FromMinutes(10);

        private readonly Stopwatch clientStopwatch = new();

        public long ClientMilliseconds => clientStopwatch.ElapsedMilliseconds;

        public async IAsyncEnumerable<List<ItemKey>> SendItemsAsync(IEnumerable<ItemKey> ids, bool fullSync)
        {
            var idsByType = ids.GroupBy((id) => id.Type).ToDictionary((g) => g.Key, (g) => g.Select((id) => id.ItemId).ToArray());
            await using var chunks = PrepareChunks(idsByType, fullSync).GetAsyncEnumerator();

            // the next chunk is read while the client applies the current one: reads mostly wait on the disk
            var next = ReadChunkAsync(chunks);
            try
            {
                var itemsProcessed = 0;
                var sent = Send(await next, itemsProcessed);
                next = ReadChunkAsync(chunks);
                while (sent is { } current)
                {
                    if (!await WaitForClientAsync(current.Ack)) throw new HubException("Client rejected sent items.");
                    itemsProcessed += current.SentIds.Count;
                    // the next chunk goes out before these ids are handed back, so the client applies it while the caller handles them
                    sent = Send(await next, itemsProcessed);
                    next = ReadChunkAsync(chunks);
                    if (current.SentIds.Count > 0) yield return current.SentIds;
                }
            }
            finally
            {
                // the enumerator can't be disposed while a read is still using it, and if sending
                // already failed, the read's own failure isn't the one to report
                try { await next; } catch { }
            }
        }

        public async Task SendMonographsAsync(IReadOnlyCollection<MonographMetadata> monographs)
        {
            if (monographs.Count > 0 && !await WaitForClientAsync(client.SendMonographs(monographs))) throw new HubException("Client rejected monographs.");
        }

        public async Task SendInboxItemsAsync(IReadOnlyCollection<InboxSyncItem> inboxItems)
        {
            if (inboxItems.Count > 0 && !await WaitForClientAsync(client.SendInboxItems(inboxItems))) throw new HubException("Client rejected inbox items.");
        }

        private async Task<bool> WaitForClientAsync(Task<bool> ack)
        {
            clientStopwatch.Start();
            try
            {
                return await ack.WaitAsync(AckTimeout);
            }
            finally
            {
                clientStopwatch.Stop();
            }
        }

        /// <summary>
        /// Groups the items to send into chunks by their stored size, reading only ids and sizes, so a fetch
        /// holds one chunk of items at a time instead of a whole cursor batch.
        /// </summary>
        private async IAsyncEnumerable<(CollectionDef Def, List<ObjectId> Ids)> PrepareChunks(Dictionary<string, string[]> idsByType, bool fullSync)
        {
            foreach (var def in collections)
            {
                var filteredIds = idsByType.GetValueOrDefault(def.Key) ?? [];
                if (!fullSync && filteredIds.Length == 0) continue;

                using var cursor = await def.Repository.FindItemSizes(userId, filteredIds, fullSync);

                var chunk = new List<ObjectId>();
                long totalBytes = 0;

                while (await cursor.MoveNextAsync())
                {
                    foreach (var item in cursor.Current)
                    {
                        chunk.Add(item.Id);
                        totalBytes += item.Size;
                        if (totalBytes >= MaxChunkBytes || chunk.Count >= MaxChunkItems)
                        {
                            yield return (def, chunk);
                            totalBytes = 0;
                            chunk = [];
                        }
                    }
                }
                if (chunk.Count > 0) yield return (def, chunk);
            }
        }

        private async Task<(CollectionDef Def, List<RawSyncItem> Items)?> ReadChunkAsync(IAsyncEnumerator<(CollectionDef Def, List<ObjectId> Ids)> chunks)
        {
            if (!await chunks.MoveNextAsync()) return null;
            var (def, objectIds) = chunks.Current;
            return (def, await def.Repository.FindItemsByObjectIds(userId, objectIds));
        }

        private (Task<bool> Ack, List<ItemKey> SentIds)? Send((CollectionDef Def, List<RawSyncItem> Items)? chunk, int itemsProcessed)
        {
            if (chunk is not { } c) return null;
            var sentIds = c.Items.Select((i) => new ItemKey(i.ItemId, c.Def.Key)).ToList();
            // items deleted since their sizes were read
            if (c.Items.Count == 0) return (Task.FromResult(true), sentIds);

            var ack = client.SendItems(new SyncTransferRawItemV2
            {
                Items = c.Items,
                Type = c.Def.Key,
                Count = itemsProcessed + c.Items.Count
            });
            return (ack, sentIds);
        }
    }
}
