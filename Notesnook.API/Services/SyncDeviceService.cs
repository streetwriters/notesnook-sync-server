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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Notesnook.API.Services
{
    public class SyncDeviceService
    {
        private static string UserSyncDirectoryPath(string userId) => Path.Join("sync", userId);
        private static string UserDeviceDirectoryPath(string userId, string deviceId) => Path.Join(SyncDeviceService.UserSyncDirectoryPath(userId), deviceId);

        private static string PendingIdsFilePath(string userId, string deviceId) => Path.Join(SyncDeviceService.UserDeviceDirectoryPath(userId, deviceId), "pending");

        private static string UnsyncedIdsFilePath(string userId, string deviceId) => Path.Join(SyncDeviceService.UserDeviceDirectoryPath(userId, deviceId), "unsynced");

        private static string ResetSyncFilePath(string userId, string deviceId) => Path.Join(SyncDeviceService.UserDeviceDirectoryPath(userId, deviceId), "reset-sync");

        public static async Task<string[]> GetUnsyncedIdsAsync(string userId, string deviceId)
        {
            try
            {
                return await File.ReadAllLinesAsync(UnsyncedIdsFilePath(userId, deviceId));
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static async Task<string[]> FetchUnsyncedIdsAsync(string userId, string deviceId)
        {
            if (IsSyncReset(userId, deviceId)) return Array.Empty<string>();
            if (UnsyncedIdsFileLocks.TryGetValue(deviceId, out SemaphoreSlim fileLock) && fileLock.CurrentCount == 0)
                await fileLock.WaitAsync();
            try
            {
                var unsyncedIds = await GetUnsyncedIdsAsync(userId, deviceId);
                if (IsSyncPending(userId, deviceId))
                {
                    unsyncedIds = unsyncedIds.Union(await File.ReadAllLinesAsync(PendingIdsFilePath(userId, deviceId))).ToArray();
                }

                if (unsyncedIds.Length == 0) return Array.Empty<string>();

                File.Delete(UnsyncedIdsFilePath(userId, deviceId));
                await File.WriteAllLinesAsync(PendingIdsFilePath(userId, deviceId), unsyncedIds);

                return unsyncedIds;
            }
            catch
            {
                return Array.Empty<string>();
            }
            finally
            {
                if (fileLock != null && fileLock.CurrentCount == 0) fileLock.Release();
            }
        }


        public static async Task WritePendingIdsAsync(string userId, string deviceId, IEnumerable<string> ids)
        {
            await File.WriteAllLinesAsync(PendingIdsFilePath(userId, deviceId), ids);
        }

        public static bool IsSyncReset(string userId, string deviceId)
        {
            return File.Exists(ResetSyncFilePath(userId, deviceId));
        }

        public static bool IsSyncPending(string userId, string deviceId)
        {
            return File.Exists(PendingIdsFilePath(userId, deviceId));
        }

        public static bool IsUnsynced(string userId, string deviceId)
        {
            return File.Exists(UnsyncedIdsFilePath(userId, deviceId));
        }

        public static void Reset(string userId, string deviceId)
        {
            File.Delete(ResetSyncFilePath(userId, deviceId));
            File.Delete(PendingIdsFilePath(userId, deviceId));
        }

        public static bool IsDeviceRegistered(string userId, string deviceId)
        {
            return Directory.Exists(UserDeviceDirectoryPath(userId, deviceId));
        }

        public static IEnumerable<string> ListDevices(string userId)
        {
            return Directory.EnumerateDirectories(UserSyncDirectoryPath(userId)).Select((path) => Path.GetFileName(path));
        }

        public static void ResetDevices(string userId)
        {
            if (File.Exists(UserSyncDirectoryPath(userId))) File.Delete(UserSyncDirectoryPath(userId));
            Directory.CreateDirectory(UserSyncDirectoryPath(userId));
        }

        private static readonly Dictionary<string, SemaphoreSlim> UnsyncedIdsFileLocks = new();
        public static async Task AddIdsToOtherDevicesAsync(string userId, string deviceId, List<string> ids)
        {
            foreach (var id in ListDevices(userId))
            {
                if (id == deviceId || IsSyncReset(userId, id)) continue;
                if (!UnsyncedIdsFileLocks.TryGetValue(id, out SemaphoreSlim fileLock))
                {
                    fileLock = new SemaphoreSlim(1, 1);
                    UnsyncedIdsFileLocks.Add(id, fileLock);
                }

                await fileLock.WaitAsync();
                try
                {
                    if (!IsDeviceRegistered(userId, id)) Directory.CreateDirectory(UserDeviceDirectoryPath(userId, id));

                    var oldIds = await GetUnsyncedIdsAsync(userId, id);
                    await File.WriteAllLinesAsync(UnsyncedIdsFilePath(userId, id), ids.Union(oldIds));
                }
                finally
                {
                    fileLock.Release();
                }
            }

        }

        public static void RegisterDevice(string userId, string deviceId)
        {
            Directory.CreateDirectory(UserDeviceDirectoryPath(userId, deviceId));
            File.Create(ResetSyncFilePath(userId, deviceId)).Close();
        }

        public static void UnregisterDevice(string userId, string deviceId)
        {
            try
            {
                Directory.Delete(UserDeviceDirectoryPath(userId, deviceId), true);
            }
            catch { }
        }
    }
}