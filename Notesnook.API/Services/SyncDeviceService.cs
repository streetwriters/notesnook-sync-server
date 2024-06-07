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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Notesnook.API.Services
{
    public struct SyncDevice(ref string userId, ref string deviceId)
    {
        public readonly string DeviceId = deviceId;
        public readonly string UserId = userId;

        private string userSyncDirectoryPath = null;
        public string UserSyncDirectoryPath
        {
            get
            {
                userSyncDirectoryPath ??= Path.Join("sync", UserId);
                return userSyncDirectoryPath;
            }
        }
        private string userDeviceDirectoryPath = null;
        public string UserDeviceDirectoryPath
        {
            get
            {
                userDeviceDirectoryPath ??= Path.Join(UserSyncDirectoryPath, DeviceId);
                return userDeviceDirectoryPath;
            }
        }
        private string pendingIdsFilePath = null;
        public string PendingIdsFilePath
        {
            get
            {
                pendingIdsFilePath ??= Path.Join(UserDeviceDirectoryPath, "pending");
                return pendingIdsFilePath;
            }
        }
        private string unsyncedIdsFilePath = null;
        public string UnsyncedIdsFilePath
        {
            get
            {
                unsyncedIdsFilePath ??= Path.Join(UserDeviceDirectoryPath, "unsynced");
                return unsyncedIdsFilePath;
            }
        }
        private string resetSyncFilePath = null;
        public string ResetSyncFilePath
        {
            get
            {
                resetSyncFilePath ??= Path.Join(UserDeviceDirectoryPath, "reset-sync");
                return resetSyncFilePath;
            }
        }
    }
    public class SyncDeviceService(SyncDevice device)
    {
        public async Task<string[]> GetUnsyncedIdsAsync()
        {
            try
            {
                return await File.ReadAllLinesAsync(device.UnsyncedIdsFilePath);
            }
            catch { return []; }
        }

        public async Task<string[]> GetUnsyncedIdsAsync(string deviceId)
        {
            try
            {
                return await File.ReadAllLinesAsync(Path.Join(device.UserSyncDirectoryPath, deviceId, "unsynced"));
            }
            catch { return []; }
        }

        public async Task<string[]> FetchUnsyncedIdsAsync()
        {
            if (IsSyncReset()) return Array.Empty<string>();
            if (UnsyncedIdsFileLocks.TryGetValue(device.DeviceId, out SemaphoreSlim fileLock) && fileLock.CurrentCount == 0)
                await fileLock.WaitAsync();
            try
            {
                var unsyncedIds = await GetUnsyncedIdsAsync();
                if (IsSyncPending())
                {
                    unsyncedIds = unsyncedIds.Union(await File.ReadAllLinesAsync(device.PendingIdsFilePath)).ToArray();
                }

                if (unsyncedIds.Length == 0) return [];

                File.Delete(device.UnsyncedIdsFilePath);
                await File.WriteAllLinesAsync(device.PendingIdsFilePath, unsyncedIds);

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


        public async Task WritePendingIdsAsync(IEnumerable<string> ids)
        {
            await File.WriteAllLinesAsync(device.PendingIdsFilePath, ids);
        }

        public bool IsSyncReset()
        {
            return File.Exists(device.ResetSyncFilePath);
        }
        public bool IsSyncReset(string deviceId)
        {
            return File.Exists(Path.Join(device.UserSyncDirectoryPath, deviceId, "reset-sync"));
        }

        public bool IsSyncPending()
        {
            return File.Exists(device.PendingIdsFilePath);
        }

        public bool IsUnsynced()
        {
            return File.Exists(device.UnsyncedIdsFilePath);
        }

        public void Reset()
        {
            File.Delete(device.ResetSyncFilePath);
            File.Delete(device.PendingIdsFilePath);
        }

        public bool IsDeviceRegistered()
        {
            return Directory.Exists(device.UserDeviceDirectoryPath);
        }
        public bool IsDeviceRegistered(string deviceId)
        {
            return Directory.Exists(Path.Join(device.UserSyncDirectoryPath, deviceId));
        }

        public string[] ListDevices()
        {
            return Directory.GetDirectories(device.UserSyncDirectoryPath).Select((path) => path[(path.LastIndexOf(Path.DirectorySeparatorChar) + 1)..]).ToArray();
        }

        public void ResetDevices()
        {
            if (File.Exists(device.UserSyncDirectoryPath)) File.Delete(device.UserSyncDirectoryPath);
            Directory.CreateDirectory(device.UserSyncDirectoryPath);
        }

        private readonly ConcurrentDictionary<string, SemaphoreSlim> UnsyncedIdsFileLocks = [];
        public async Task AddIdsToOtherDevicesAsync(List<string> ids)
        {
            await Parallel.ForEachAsync(ListDevices(), async (id, ct) =>
            {
                if (id == device.DeviceId || IsSyncReset(id)) return;
                if (!UnsyncedIdsFileLocks.TryGetValue(id, out SemaphoreSlim fileLock))
                {
                    fileLock = UnsyncedIdsFileLocks.AddOrUpdate(id, (id) => new SemaphoreSlim(1, 1), (id, old) => new SemaphoreSlim(1, 1));
                }

                await fileLock.WaitAsync(ct);
                try
                {
                    if (!IsDeviceRegistered(id)) Directory.CreateDirectory(Path.Join(device.UserSyncDirectoryPath, id));

                    var oldIds = await GetUnsyncedIdsAsync(id);
                    await File.WriteAllLinesAsync(Path.Join(device.UserSyncDirectoryPath, id, "unsynced"), ids.Union(oldIds), ct);
                }
                finally
                {
                    fileLock.Release();
                }
            });
        }

        public void RegisterDevice()
        {
            Directory.CreateDirectory(device.UserDeviceDirectoryPath);
            File.Create(device.ResetSyncFilePath).Close();
        }

        public void UnregisterDevice()
        {
            try
            {
                Directory.Delete(device.UserDeviceDirectoryPath, true);
            }
            catch { }
        }
    }
}