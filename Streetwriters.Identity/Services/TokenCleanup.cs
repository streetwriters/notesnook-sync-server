// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using IdentityServer4.MongoDB.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Streetwriters.Identity.Services
{
    public class TokenCleanup
    {
        private readonly IPersistedGrantDbContext _persistedGrantDbContext;
        private readonly ILogger<TokenCleanup> _logger;

        public TokenCleanup(IPersistedGrantDbContext persistedGrantDbContext, ILogger<TokenCleanup> logger)
        {
            _persistedGrantDbContext = persistedGrantDbContext;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Method to clear expired persisted grants.
        /// </summary>
        /// <returns></returns>
        public async Task RemoveExpiredGrantsAsync()
        {
            try
            {
                _logger.LogTrace("Querying for expired grants to remove");

                await RemoveGrantsAsync();
                //TODO: await RemoveDeviceCodesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception removing expired grants: {exception}", ex.Message);
            }
        }

        /// <summary>
        /// Removes the stale persisted grants.
        /// </summary>
        /// <returns></returns>
        protected virtual async Task RemoveGrantsAsync()
        {
            await _persistedGrantDbContext.RemoveExpired();
        }
    }
}