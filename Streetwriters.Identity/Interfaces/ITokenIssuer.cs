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

using System.Threading.Tasks;
using Streetwriters.Common.Models;

namespace Streetwriters.Identity.Interfaces
{
    public record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresIn, string Scope);

    /// <summary>Issues tokens to users outside of the token endpoint, e.g. on signup.</summary>
    public interface ITokenIssuer
    {
        /// <summary>Starts a session for the user: an access token with all the client's scopes, and a refresh token.</summary>
        Task<IssuedTokens?> IssueUserTokensAsync(User user, string clientId, int lifetime);

        /// <summary>An access token with all the client's scopes, without a session to refresh.</summary>
        Task<string> IssueAccessTokenAsync(User user, string clientId, int lifetime, string[] authenticationMethods);
    }
}
