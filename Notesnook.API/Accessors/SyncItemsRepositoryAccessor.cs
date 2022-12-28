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