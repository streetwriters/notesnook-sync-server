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
    public struct SyncDevice(string userId, string deviceId)
    {
        public readonly string DeviceId => deviceId;
        public readonly string UserId => userId;

        public string UserSyncDirectoryPath = CreateFilePath(userId);
        public string UserDeviceDirectoryPath = CreateFilePath(userId, deviceId);
        public string PendingIdsFilePath = CreateFilePath(userId, deviceId, "pending");
        public string UnsyncedIdsFilePath = CreateFilePath(userId, deviceId, "unsynced");
        public string ResetSyncFilePath = CreateFilePath(userId, deviceId, "reset-sync");

        public readonly long LastAccessTime
        {
            get => long.Parse(GetMetadata("LastAccessTime") ?? "0");
            set => SetMetadata("LastAccessTime", value.ToString());
        }

        private static string CreateFilePath(string userId, string? deviceId = null, string? metadataKey = null)
        {
            return Path.Join("sync", userId, deviceId, metadataKey);
        }

        private readonly string? GetMetadata(string metadataKey)
        {
            var path = CreateFilePath(userId, deviceId, metadataKey);
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path);
        }

        private readonly void SetMetadata(string metadataKey, string value)
        {
            try
            {
                var path = CreateFilePath(userId, deviceId, metadataKey);
                File.WriteAllText(path, value);
            }
            catch (DirectoryNotFoundException) { }
        }
    }

    public class SyncDeviceService(SyncDevice device)
    {
        public string[] GetUnsyncedIds()
        {
            try
            {
                return File.ReadAllLines(device.UnsyncedIdsFilePath);
            }
            catch { return []; }
        }

        public string[] GetUnsyncedIds(string deviceId)
        {
            try
            {
                return File.ReadAllLines(Path.Join(device.UserSyncDirectoryPath, deviceId, "unsynced"));
            }
            catch { return []; }
        }

        public string[] FetchUnsyncedIds()
        {
            if (IsSyncReset()) return [];
            try
            {
                var unsyncedIds = GetUnsyncedIds();
                lock (device.DeviceId)
                {
                    if (IsSyncPending())
                    {
                        unsyncedIds = unsyncedIds.Union(File.ReadAllLines(device.PendingIdsFilePath)).ToArray();
                    }

                    if (unsyncedIds.Length == 0) return [];

                    File.Delete(device.UnsyncedIdsFilePath);
                    File.WriteAllLines(device.PendingIdsFilePath, unsyncedIds);
                }
                return unsyncedIds;
            }
            catch
            {
                return [];
            }
        }

        public void WritePendingIds(IEnumerable<string> ids)
        {
            lock (device.DeviceId)
            {
                File.WriteAllLines(device.PendingIdsFilePath, ids);
            }
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
            try
            {
                lock (device.UserId)
                {
                    File.Delete(device.ResetSyncFilePath);
                    File.Delete(device.PendingIdsFilePath);
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
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
            lock (device.UserId)
            {
                if (File.Exists(device.UserSyncDirectoryPath)) File.Delete(device.UserSyncDirectoryPath);
                Directory.CreateDirectory(device.UserSyncDirectoryPath);
            }
        }

        public void AddIdsToOtherDevices(List<string> ids)
        {
            device.LastAccessTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (string id in ListDevices())
            {
                if (id == device.DeviceId || IsSyncReset(id)) continue;

                lock (id)
                {
                    if (!IsDeviceRegistered(id)) Directory.CreateDirectory(Path.Join(device.UserSyncDirectoryPath, id));

                    var oldIds = GetUnsyncedIds(id);
                    File.WriteAllLines(Path.Join(device.UserSyncDirectoryPath, id, "unsynced"), ids.Union(oldIds));
                }
            }
        }

        public async Task AddIdsToAllDevicesAsync(List<string> ids)
        {
            await Parallel.ForEachAsync(ListDevices(), async (id, ct) =>
            {
                if (IsSyncReset(id)) return;
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
            lock (device.UserId)
            {
                if (Directory.Exists(device.UserDeviceDirectoryPath))
                    Directory.Delete(device.UserDeviceDirectoryPath, true);
                Directory.CreateDirectory(device.UserDeviceDirectoryPath);
                File.Create(device.ResetSyncFilePath).Close();
                device.LastAccessTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        public void UnregisterDevice()
        {
            lock (device.UserId)
            {
                if (!Path.Exists(device.UserDeviceDirectoryPath)) return;
                Directory.Delete(device.UserDeviceDirectoryPath, true);
            }
        }
    }
}