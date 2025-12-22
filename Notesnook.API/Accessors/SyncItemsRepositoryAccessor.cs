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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Repositories;
using Streetwriters.Data.Interfaces;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Accessors
{
    public class SyncItemsRepositoryAccessor : ISyncItemsRepositoryAccessor
    {
        public SyncItemsRepository Notes { get; }
        public SyncItemsRepository Notebooks { get; }
        public SyncItemsRepository Shortcuts { get; }
        public SyncItemsRepository Relations { get; }
        public SyncItemsRepository Reminders { get; }
        public SyncItemsRepository Contents { get; }
        public SyncItemsRepository LegacySettings { get; }
        public SyncItemsRepository Settings { get; }
        public SyncItemsRepository Attachments { get; }
        public SyncItemsRepository Colors { get; }
        public SyncItemsRepository Vaults { get; }
        public SyncItemsRepository Tags { get; }
        public Repository<UserSettings> UsersSettings { get; }
        public Repository<Monograph> Monographs { get; }
        public Repository<InboxApiKey> InboxApiKey { get; }
        public Repository<InboxSyncItem> InboxItems { get; }
        public Repository<SyncDevice> SyncDevices { get; }
        public Repository<DeviceIdsChunk> DeviceIdsChunks { get; }

        public SyncItemsRepositoryAccessor(IDbContext dbContext,

        [FromKeyedServices(Collections.NotebooksKey)]
        IMongoCollection<SyncItem> notebooks,
        [FromKeyedServices(Collections.NotesKey)]
        IMongoCollection<SyncItem> notes,
        [FromKeyedServices(Collections.ContentKey)]
        IMongoCollection<SyncItem> content,
        [FromKeyedServices(Collections.SettingsKey)]
        IMongoCollection<SyncItem> settings,
        [FromKeyedServices(Collections.LegacySettingsKey)]
        IMongoCollection<SyncItem> legacySettings,
        [FromKeyedServices(Collections.AttachmentsKey)]
        IMongoCollection<SyncItem> attachments,
        [FromKeyedServices(Collections.ShortcutsKey)]
        IMongoCollection<SyncItem> shortcuts,
        [FromKeyedServices(Collections.RemindersKey)]
        IMongoCollection<SyncItem> reminders,
        [FromKeyedServices(Collections.RelationsKey)]
        IMongoCollection<SyncItem> relations,
        [FromKeyedServices(Collections.ColorsKey)]
        IMongoCollection<SyncItem> colors,
        [FromKeyedServices(Collections.VaultsKey)]
        IMongoCollection<SyncItem> vaults,
        [FromKeyedServices(Collections.TagsKey)]
        IMongoCollection<SyncItem> tags,

        Repository<UserSettings> usersSettings,
        Repository<Monograph> monographs,
        Repository<InboxApiKey> inboxApiKey,
        Repository<InboxSyncItem> inboxItems,
        Repository<SyncDevice> syncDevices,
        Repository<DeviceIdsChunk> deviceIdsChunks,
        ILogger<SyncItemsRepository> logger)
        {
            UsersSettings = usersSettings;
            Monographs = monographs;
            InboxApiKey = inboxApiKey;
            InboxItems = inboxItems;
            SyncDevices = syncDevices;
            DeviceIdsChunks = deviceIdsChunks;
            Notebooks = new SyncItemsRepository(dbContext, notebooks, logger);
            Notes = new SyncItemsRepository(dbContext, notes, logger);
            Contents = new SyncItemsRepository(dbContext, content, logger);
            Settings = new SyncItemsRepository(dbContext, settings, logger);
            LegacySettings = new SyncItemsRepository(dbContext, legacySettings, logger);
            Attachments = new SyncItemsRepository(dbContext, attachments, logger);
            Shortcuts = new SyncItemsRepository(dbContext, shortcuts, logger);
            Reminders = new SyncItemsRepository(dbContext, reminders, logger);
            Relations = new SyncItemsRepository(dbContext, relations, logger);
            Colors = new SyncItemsRepository(dbContext, colors, logger);
            Vaults = new SyncItemsRepository(dbContext, vaults, logger);
            Tags = new SyncItemsRepository(dbContext, tags, logger);
        }
    }
}