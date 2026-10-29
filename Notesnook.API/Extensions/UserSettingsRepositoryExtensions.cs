using System.Threading.Tasks;
using MongoDB.Driver;
using Notesnook.API.Models;
using Streetwriters.Data.Repositories;

namespace Notesnook.API.Extensions
{
    public static class UserSettingsRepositoryExtensions
    {
        public static async Task<bool> IsUserDeletedAsync(this Repository<UserSettings> settings, string userId)
            => !await settings.Collection.Find((s) => s.UserId == userId && s.DeletedAt == null).AnyAsync();
    }
}
