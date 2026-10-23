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
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using Streetwriters.Common;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>
    /// Revocation as IdentityServer4 did it: revoking a refresh token also revokes every access token of
    /// the user on the client, while their other refresh tokens keep working.
    /// </summary>
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class RevocationController(IOpenIddictTokenManager tokens) : ControllerBase
    {
        [HttpPost("~/connect/revocation")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> RevokeAsync([FromForm(Name = "client_id")] string? clientId, [FromForm] string? token)
        {
            if (Clients.FindClientById(clientId) == null) return BadRequest(new { error = "invalid_client" });
            if (string.IsNullOrEmpty(token)) return BadRequest(new { error = "invalid_request" });

            var entry = await tokens.FindByReferenceIdAsync(token);
            if (entry == null) return Ok();

            if (await tokens.HasTypeAsync(entry, TokenTypeIdentifiers.RefreshToken) && await tokens.GetSubjectAsync(entry) is string subject)
            {
                await foreach (var other in tokens.FindBySubjectAsync(subject))
                {
                    if (await tokens.HasTypeAsync(other, TokenTypeIdentifiers.AccessToken)) await tokens.TryRevokeAsync(other);
                }
            }
            await tokens.TryRevokeAsync(entry);
            return Ok();
        }
    }
}
