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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;

namespace Streetwriters.Identity.Services
{
    // WAMP callees are long-lived, so each call gets its own scope that is disposed when the call ends.
    public class ScopedUserAccountService(IServiceProvider services) : IUserAccountService
    {
        private async Task<T> WithServiceAsync<T>(Func<IUserAccountService, Task<T>> action)
        {
            await using var scope = services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<IUserAccountService>());
        }

        public Task<UserModel?> GetUserAsync(string clientId, string userId) => WithServiceAsync((s) => s.GetUserAsync(clientId, userId));
        public Task<bool> UserExistsAsync(string userId) => WithServiceAsync((s) => s.UserExistsAsync(userId));
        public Task UnregisterUserAsync(string clientId, string userId) => WithServiceAsync(async (s) => { await s.UnregisterUserAsync(clientId, userId); return true; });
        public Task<bool> ChangePasswordAsync(string userId, string oldPassword, string newPassword) => WithServiceAsync((s) => s.ChangePasswordAsync(userId, oldPassword, newPassword));
        public Task<bool> ResetPasswordAsync(string userId, string newPassword) => WithServiceAsync((s) => s.ResetPasswordAsync(userId, newPassword));
        public Task<bool> VerifyPasswordAsync(string userId, string password) => WithServiceAsync((s) => s.VerifyPasswordAsync(userId, password));
        public Task<bool> ClearSessionsAsync(string userId, string clientId, bool all, string jti, string? refreshToken) => WithServiceAsync((s) => s.ClearSessionsAsync(userId, clientId, all, jti, refreshToken));
        public Task<SignupResponse> CreateUserAsync(string clientId, string email, string password, string? userAgent = null) => WithServiceAsync((s) => s.CreateUserAsync(clientId, email, password, userAgent));
    }
}
