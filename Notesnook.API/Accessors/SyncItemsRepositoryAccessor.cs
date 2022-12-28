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

using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Repositories;
using Streetwriters.Common.Models;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Accessors
{
    public class SyncItemsRepositoryAccessor : ISyncItemsRepositoryAccessor
    {
        public SyncItemsRepository<Note> Notes { get; }
        public SyncItemsRepository<Notebook> Notebooks { get; }
        public SyncItemsRepository<Shortcut> Shortcuts { get; }
        public SyncItemsRepository<Relation> Relations { get; }
        public SyncItemsRepository<Reminder> Reminders { get; }
        public SyncItemsRepository<Content> Contents { get; }
        public SyncItemsRepository<Setting> Settings { get; }
        public SyncItemsRepository<Attachment> Attachments { get; }
        public Repository<UserSettings> UsersSettings { get; }
        public Repository<Monograph> Monographs { get; }

        public SyncItemsRepositoryAccessor(SyncItemsRepository<Note> _notes,
        SyncItemsRepository<Notebook> _notebooks,
        SyncItemsRepository<Content> _content,
        SyncItemsRepository<Setting> _settings,
        SyncItemsRepository<Attachment> _attachments,
        SyncItemsRepository<Shortcut> _shortcuts,
        SyncItemsRepository<Relation> _relations,
        SyncItemsRepository<Reminder> _reminders,
        Repository<UserSettings> _usersSettings,
        Repository<Monograph> _monographs)
        {
            Notebooks = _notebooks;
            Notes = _notes;
            Contents = _content;
            Settings = _settings;
            Attachments = _attachments;
            UsersSettings = _usersSettings;
            Monographs = _monographs;
            Shortcuts = _shortcuts;
            Reminders = _reminders;
            Relations = _relations;
        }
    }
}