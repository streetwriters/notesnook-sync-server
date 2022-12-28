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

using System;
using System.Threading.Tasks;
using IdentityServer4.Models;
using IdentityServer4.Services;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;

namespace Streetwriters.Identity.Services
{
    public class CustomRefreshTokenService : DefaultRefreshTokenService
    {
        public CustomRefreshTokenService(IRefreshTokenStore refreshTokenStore, IProfileService profile, ISystemClock clock, ILogger<DefaultRefreshTokenService> logger) : base(refreshTokenStore, profile, clock, logger)
        {
        }

        protected override Task<bool> AcceptConsumedTokenAsync(RefreshToken refreshToken)
        {
            // Allow refresh token replay for 1 day.
            // if (refreshToken.ConsumedTime?.ToUniversalTime().AddDays(1) < DateTime.UtcNow) return Task.FromResult(false);
            return Task.FromResult(true);
        }
    }
}