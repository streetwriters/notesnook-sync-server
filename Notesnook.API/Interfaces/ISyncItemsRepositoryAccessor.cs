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