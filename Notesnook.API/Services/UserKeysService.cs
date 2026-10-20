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
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading.Tasks;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Streetwriters.Common;
using Streetwriters.Common.Messages;
using Streetwriters.Common.Models;

namespace Notesnook.API.Services
{
    public enum KeyWriteResult
    {
        Success,
        PasswordChangeInProgress,
        /// <summary>
        /// The keys were wrapped with a master key from before the last password change.
        /// </summary>
        WrappedWithOldPassword,
        /// <summary>
        /// The keys were rewrapped from an outdated copy of the user.
        /// </summary>
        KeysChanged
    }

    public class UserKeysService(ISyncItemsRepositoryAccessor syncItemsRepositoryAccessor) : IUserKeysService
    {
        private ISyncItemsRepositoryAccessor Repositories { get; } = syncItemsRepositoryAccessor;

        /// <summary>
        /// Clients can't discard pending keys staged more recently than this,
        /// as the password change that staged them may still be running and
        /// set the password they're for.
        /// </summary>
        private static readonly long PendingKeysTimeout = (long)TimeSpan.FromMinutes(5).TotalMilliseconds;

        private static readonly (Expression<Func<UserSettings, EncryptedData?>> Field, Func<UserKeys, EncryptedData?> Get)[] KeyFields =
        [
            ((u) => u.AttachmentsKey, (k) => k.AttachmentsKey),
            ((u) => u.MonographPasswordsKey, (k) => k.MonographPasswordsKey),
            ((u) => u.DataEncryptionKey, (k) => k.DataEncryptionKey),
            ((u) => u.LegacyDataEncryptionKey, (k) => k.LegacyDataEncryptionKey),
        ];

        /// <summary>
        /// Stages keys rewrapped for a password change. They replace the
        /// current keys once the password has changed. Keys that are already
        /// pending block this until a login resolves them: the password they
        /// are for might have been set.
        /// </summary>
        /// <param name="stagedAt">identifies the staged keys when committing or discarding them</param>
        public async Task<KeyWriteResult> StageKeysAsync(string userId, UserKeys keys, long? revision, long stagedAt)
        {
            var filter = UserFilter(userId) & NoPendingKeys() & HasNoKeysOutside(keys);
            if (revision != null) filter &= HasRevision(revision.Value);

            var result = await Repositories.UsersSettings.Collection.UpdateOneAsync(filter, Builders<UserSettings>.Update
                .Set((u) => u.PendingKeys, keys)
                .Set((u) => u.PendingKeysSince, stagedAt)
                .Inc((u) => u.Revision, 1));
            if (result.MatchedCount > 0) return KeyWriteResult.Success;

            var blocker = await GetKeyWriteBlockerAsync(userId, null);
            return blocker == KeyWriteResult.Success ? KeyWriteResult.KeysChanged : blocker;
        }

        /// <summary>
        /// Replaces the user's keys with the keys staged at <paramref name="stagedAt"/>.
        /// </summary>
        public async Task<KeyWriteResult> CommitStagedKeysAsync(string userId, long stagedAt)
        {
            return await PromotePendingKeysAsync(userId, StagedAt(stagedAt));
        }

        /// <summary>
        /// Drops the keys staged at <paramref name="stagedAt"/> and keeps the current keys.
        /// </summary>
        public async Task DiscardStagedKeysAsync(string userId, long stagedAt)
        {
            await Repositories.UsersSettings.Collection.UpdateOneAsync(UserFilter(userId) & StagedAt(stagedAt), ClearPendingKeys());
        }

        /// <summary>
        /// Commits the pending keys of a password change that failed midway,
        /// for a client that logged in with a password that unwraps them.
        /// Logging in proves that password is set.
        /// </summary>
        /// <param name="revision">revision of the user whose pending keys the client checked</param>
        public async Task<KeyWriteResult> CommitPendingKeysAsync(string userId, long revision)
        {
            return await PromotePendingKeysAsync(userId, Builders<UserSettings>.Filter.Eq((u) => u.Revision, revision));
        }

        /// <summary>
        /// Discards the pending keys of a password change that failed midway,
        /// for a client that logged in with a password that doesn't unwrap
        /// them. Logging in proves which password is set, so the pending keys
        /// are for a password that was never set, unless the password change
        /// that staged them is still running.
        /// </summary>
        /// <param name="revision">revision of the user whose pending keys the client checked</param>
        public async Task<KeyWriteResult> DiscardPendingKeysAsync(string userId, long revision)
        {
            var hasRevision = Builders<UserSettings>.Filter.Eq((u) => u.Revision, revision);
            var timedOut = Builders<UserSettings>.Filter.Lt((u) => u.PendingKeysSince, Now() - PendingKeysTimeout);
            var result = await Repositories.UsersSettings.Collection.UpdateOneAsync(UserFilter(userId) & hasRevision & timedOut, ClearPendingKeys());
            if (result.MatchedCount > 0) return KeyWriteResult.Success;

            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId) ?? throw new Exception("User not found.");
            return userSettings.Revision == revision && userSettings.PendingKeys != null
                ? KeyWriteResult.PasswordChangeInProgress
                : KeyWriteResult.KeysChanged;
        }

        private async Task<KeyWriteResult> PromotePendingKeysAsync(string userId, FilterDefinition<UserSettings> pendingKeysFilter)
        {
            var filter = UserFilter(userId) & pendingKeysFilter & Builders<UserSettings>.Filter.Ne((u) => u.PendingKeys, null);
            var userSettings = await Repositories.UsersSettings.Collection.Find(filter).FirstOrDefaultAsync();
            if (userSettings == null) return KeyWriteResult.KeysChanged;
            var keys = userSettings.PendingKeys!;

            var updates = KeyFields
                .Where((k) => k.Get(keys) != null)
                .Select((k) => Builders<UserSettings>.Update.Set(k.Field, k.Get(keys)))
                .ToList();
            if (IsValid(keys.InboxKeys))
                updates.Add(Builders<UserSettings>.Update.Set((u) => u.InboxKeys, keys.InboxKeys));
            updates.Add(ClearPendingKeys());
            // ClearPendingKeys increments the revision
            updates.Add(Builders<UserSettings>.Update.Set((u) => u.MasterKeyRevision, userSettings.Revision + 1));

            // key writes are blocked while keys are pending, so the revision
            // only changes if the pending keys were committed or replaced
            var result = await Repositories.UsersSettings.Collection.UpdateOneAsync(
                filter & Builders<UserSettings>.Filter.Eq((u) => u.Revision, userSettings.Revision),
                Builders<UserSettings>.Update.Combine(updates));
            return result.MatchedCount > 0 ? KeyWriteResult.Success : KeyWriteResult.KeysChanged;
        }

        /// <summary>
        /// Stores each of the given keys only if the user doesn't have it yet,
        /// so concurrent callers all end up with the same keys.
        /// </summary>
        /// <param name="revision">revision of the user whose master key wrapped the keys</param>
        public async Task<KeyWriteResult> GetOrCreateUserKeysAsync(string userId, UserKeys keys, long? revision)
        {
            var allCreated = true;
            foreach (var (field, value) in GetKeyFields(keys))
                allCreated &= await CreateIfMissingAsync(userId, field, value, revision);

            if (IsValid(keys.InboxKeys))
            {
                if (await CreateIfMissingAsync(userId, (u) => u.InboxKeys, keys.InboxKeys!, revision))
                    await NotifyInboxKeysChangedAsync(userId);
                else allCreated = false;
            }

            // a key that wasn't created either exists already or was blocked
            return allCreated ? KeyWriteResult.Success : await GetKeyWriteBlockerAsync(userId, revision);
        }

        /// <summary>
        /// Legacy behavior of <c>PATCH /users</c>: sets the given keys only where
        /// the user doesn't have them yet. Returns false if any of them already
        /// exists with a different value or a password change is in progress.
        /// Inbox keys are always overwritten (or removed when empty) because old
        /// clients also use this to replace them.
        /// </summary>
        [Obsolete("Only for old clients (PATCH /users). Use GetOrCreateUserKeysAsync and SetInboxKeysAsync instead.")]
        public async Task<bool> AddUserKeysAsync(string userId, UserKeys keys)
        {
            await EnsureUserExistsAsync(userId);

            var added = true;
            foreach (var (field, value) in GetKeyFields(keys))
            {
                if (await CreateIfMissingAsync(userId, field, value, null)) continue;
                // sending the same key again is fine
                var filter = UserFilter(userId) & Builders<UserSettings>.Filter.Eq(field, value);
                if (await Repositories.UsersSettings.Collection.CountDocumentsAsync(filter) == 0) added = false;
            }

            if (!added) return false;

            if (keys.InboxKeys != null)
                return IsValid(keys.InboxKeys)
                    ? await SetInboxKeysAsync(userId, keys.InboxKeys, null) == KeyWriteResult.Success
                    : await RemoveInboxKeysAsync(userId);

            return true;
        }

        /// <summary>
        /// Replaces the user's inbox keys. Items encrypted with the old keys are deleted.
        /// </summary>
        /// <param name="revision">revision of the user whose master key wrapped the keys</param>
        public async Task<KeyWriteResult> SetInboxKeysAsync(string userId, InboxKeys keys, long? revision)
        {
            var result = await Repositories.UsersSettings.Collection.UpdateOneAsync(
                UserFilter(userId) & CanWriteKeys(revision),
                Builders<UserSettings>.Update.Set((u) => u.InboxKeys, keys).Inc((u) => u.Revision, 1));
            if (result.MatchedCount == 0)
            {
                var blocker = await GetKeyWriteBlockerAsync(userId, revision);
                // whatever blocked the write was resolved in the meantime
                return blocker == KeyWriteResult.Success ? KeyWriteResult.KeysChanged : blocker;
            }

            await Repositories.InboxItems.DeleteManyAsync(t => t.UserId == userId);
            await NotifyInboxKeysChangedAsync(userId);
            return KeyWriteResult.Success;
        }

        /// <summary>
        /// Removes the user's inbox keys along with their inbox API keys and items.
        /// Returns false if keys are pending.
        /// </summary>
        public async Task<bool> RemoveInboxKeysAsync(string userId)
        {
            await EnsureUserExistsAsync(userId);
            var result = await Repositories.UsersSettings.Collection.UpdateOneAsync(
                UserFilter(userId) & NoPendingKeys(),
                Builders<UserSettings>.Update.Set((u) => u.InboxKeys, null).Inc((u) => u.Revision, 1));
            if (result.MatchedCount == 0) return false;

            await Repositories.InboxApiKey.DeleteManyAsync(t => t.UserId == userId);
            await Repositories.InboxItems.DeleteManyAsync(t => t.UserId == userId);
            await NotifyInboxKeysChangedAsync(userId);
            return true;
        }

        private static bool IsValid(InboxKeys? keys)
        {
            return keys?.Public != null && keys.Private != null;
        }

        private static IEnumerable<(Expression<Func<UserSettings, EncryptedData?>> Field, EncryptedData Value)> GetKeyFields(UserKeys keys)
        {
            return KeyFields.Where((k) => k.Get(keys) != null).Select((k) => (k.Field, k.Get(keys)!));
        }

        /// <summary>
        /// Tells why a key write that matched nothing was rejected, or returns
        /// <see cref="KeyWriteResult.Success"/> if nothing blocks key writes.
        /// The writes check this atomically, see <see cref="CanWriteKeys"/>.
        /// </summary>
        private async Task<KeyWriteResult> GetKeyWriteBlockerAsync(string userId, long? revision)
        {
            var userSettings = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId) ?? throw new Exception("User not found.");
            if (userSettings.PendingKeys != null) return KeyWriteResult.PasswordChangeInProgress;
            if (revision < userSettings.MasterKeyRevision) return KeyWriteResult.WrappedWithOldPassword;
            return KeyWriteResult.Success;
        }

        /// <summary>
        /// Matches users whose keys can be written: no keys are pending and,
        /// if given, the keys were wrapped with the current master key.
        /// Pending keys block key writes until they are resolved, even after
        /// they timed out: the password might have changed, and a key written
        /// now would be missing from them.
        /// </summary>
        private static FilterDefinition<UserSettings> CanWriteKeys(long? revision)
        {
            var filter = NoPendingKeys();
            if (revision == null) return filter;
            return filter & (Builders<UserSettings>.Filter.Lte((u) => u.MasterKeyRevision, revision.Value)
                // users created before revisions were added don't have one
                | Builders<UserSettings>.Filter.Exists((u) => u.MasterKeyRevision, false));
        }

        private async Task<bool> CreateIfMissingAsync<T>(string userId, Expression<Func<UserSettings, T?>> field, T value, long? revision) where T : class
        {
            var result = await Repositories.UsersSettings.Collection.UpdateOneAsync(
                UserFilter(userId) & CanWriteKeys(revision) & Builders<UserSettings>.Filter.Eq(field, null),
                Builders<UserSettings>.Update.Set(field, value).Inc((u) => u.Revision, 1));
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Matches users who have no key that <paramref name="keys"/> leaves out.
        /// A left out key would stay wrapped with the old password.
        /// </summary>
        private static FilterDefinition<UserSettings> HasNoKeysOutside(UserKeys keys)
        {
            var f = Builders<UserSettings>.Filter;
            var filter = f.Empty;
            foreach (var key in KeyFields)
                if (key.Get(keys) == null) filter &= f.Eq(key.Field, null);
            if (!IsValid(keys.InboxKeys)) filter &= f.Eq((u) => u.InboxKeys, null);
            return filter;
        }

        private static FilterDefinition<UserSettings> UserFilter(string userId)
        {
            return Builders<UserSettings>.Filter.Eq((u) => u.UserId, userId);
        }

        private static FilterDefinition<UserSettings> HasRevision(long revision)
        {
            var filter = Builders<UserSettings>.Filter.Eq((u) => u.Revision, revision);
            // users created before revisions were added don't have one
            return revision == 0 ? filter | Builders<UserSettings>.Filter.Exists((u) => u.Revision, false) : filter;
        }

        private static FilterDefinition<UserSettings> NoPendingKeys()
        {
            return Builders<UserSettings>.Filter.Eq((u) => u.PendingKeys, null);
        }

        private static FilterDefinition<UserSettings> StagedAt(long stagedAt)
        {
            return Builders<UserSettings>.Filter.Eq((u) => u.PendingKeysSince, stagedAt);
        }

        private static UpdateDefinition<UserSettings> ClearPendingKeys()
        {
            return Builders<UserSettings>.Update
                .Set((u) => u.PendingKeys, null)
                .Set((u) => u.PendingKeysSince, null)
                .Inc((u) => u.Revision, 1);
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static async Task NotifyInboxKeysChangedAsync(string userId)
        {
            await WampServers.MessengerServer.PublishMessageAsync(MessengerServerTopics.SendSSETopic, new SendSSEMessage
            {
                OriginTokenId = null,
                UserId = userId,
                Message = new Message
                {
                    Type = "inboxUpdated",
                    Data = JsonSerializer.Serialize(new { reason = "Inbox PGP keys added, updated, or removed." })
                }
            });
        }

        private async Task EnsureUserExistsAsync(string userId)
        {
            _ = await Repositories.UsersSettings.FindOneAsync((u) => u.UserId == userId) ?? throw new Exception("User not found.");
        }
    }
}
