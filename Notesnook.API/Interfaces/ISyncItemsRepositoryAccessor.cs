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

using Notesnook.API.Models;
using Notesnook.API.Repositories;
using Streetwriters.Common.Models;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Interfaces
{
    public interface ISyncItemsRepositoryAccessor
    {
        SyncItemsRepository<Note> Notes { get; }
        SyncItemsRepository<Notebook> Notebooks { get; }
        SyncItemsRepository<Shortcut> Shortcuts { get; }
        SyncItemsRepository<Reminder> Reminders { get; }
        SyncItemsRepository<Relation> Relations { get; }
        SyncItemsRepository<Content> Contents { get; }
        SyncItemsRepository<Setting> Settings { get; }
        SyncItemsRepository<Attachment> Attachments { get; }
        Repository<UserSettings> UsersSettings { get; }
        Repository<Monograph> Monographs { get; }
    }
}