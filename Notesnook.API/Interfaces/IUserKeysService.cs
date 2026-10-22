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
using System.Threading.Tasks;
using Notesnook.API.Models;
using Notesnook.API.Services;
using Streetwriters.Common.Models;

namespace Notesnook.API.Interfaces
{
    public interface IUserKeysService
    {
        Task<KeyWriteResult> StageKeysAsync(string userId, UserKeys keys, long? revision, long stagedAt);
        Task<KeyWriteResult> CommitStagedKeysAsync(string userId, long stagedAt);
        Task DiscardStagedKeysAsync(string userId, long stagedAt);
        Task<KeyWriteResult> CommitPendingKeysAsync(string userId, long revision, long authenticatedAt);
        Task<KeyWriteResult> DiscardPendingKeysAsync(string userId, long revision, long authenticatedAt);
        Task<KeyWriteResult> GetOrCreateUserKeysAsync(string userId, UserKeys keys, long? revision);
        [Obsolete("Only for old clients (PATCH /users). Use GetOrCreateUserKeysAsync and SetInboxKeysAsync instead.")]
        Task<bool> AddUserKeysAsync(string userId, UserKeys keys);
        Task<KeyWriteResult> SetInboxKeysAsync(string userId, InboxKeys keys, long? revision);
        Task<bool> RemoveInboxKeysAsync(string userId);
    }
}
