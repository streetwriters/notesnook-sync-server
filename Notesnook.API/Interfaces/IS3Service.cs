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

using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model;
using Notesnook.API.Models;
using Notesnook.API.Models.Responses;
using Streetwriters.Common.Interfaces;

namespace Notesnook.API.Interfaces
{
    public interface IS3Service
    {
        Task DeleteObjectAsync(string userId, string name);
        Task DeleteDirectoryAsync(string userId);
        Task<long?> GetObjectSizeAsync(string userId, string name);
        string GetUploadObjectUrl(string userId, string name);
        string GetDownloadObjectUrl(string userId, string name);
        Task<MultipartUploadMeta> StartMultipartUploadAsync(string userId, string name, int parts, string uploadId = null);
        Task AbortMultipartUploadAsync(string userId, string name, string uploadId);
        Task CompleteMultipartUploadAsync(string userId, CompleteMultipartUploadRequest uploadRequest);
    }
}