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

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Streetwriters.Identity.Interfaces
{
    /// <summary>The tokens issued to users, grouped into the sessions they belong to.</summary>
    public interface ISessionStore
    {
        /// <summary>Ends the session of the access token with this jti.</summary>
        Task EndSessionAsync(string clientId, string subjectId, string jti);

        /// <summary>
        /// Ends the user's sessions on the client, except the one with the access token <paramref name="exceptJti"/>
        /// or the refresh token <paramref name="exceptRefreshToken"/>, if given.
        /// </summary>
        /// <returns>The keys under which introspection results of the ended sessions' tokens are cached.</returns>
        Task<List<string>> EndSessionsAsync(string clientId, string subjectId, string? exceptJti, string? exceptRefreshToken);

        /// <summary>Ends all of the user's sessions on all clients.</summary>
        Task EndAllSessionsAsync(string subjectId);
    }
}
