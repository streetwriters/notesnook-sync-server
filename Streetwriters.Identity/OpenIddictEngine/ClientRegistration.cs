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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Streetwriters.Identity.OpenIddictEngine
{
    /// <summary>Registers the apps' client, as Config.Clients declares it for IdentityServer4.</summary>
    public class ClientRegistration(System.IServiceProvider services) : IHostedService
    {
        public static OpenIddictApplicationDescriptor Notesnook
        {
            get
            {
                var descriptor = new OpenIddictApplicationDescriptor
                {
                    ClientId = "notesnook",
                    DisplayName = "Notesnook",
                    ClientType = ClientTypes.Public,
                };
                descriptor.Permissions.UnionWith([
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.Password,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.Prefixes.GrantType + Config.EMAIL_GRANT_TYPE,
                    Permissions.Prefixes.GrantType + Config.MFA_GRANT_TYPE,
                    Permissions.Prefixes.GrantType + Config.MFA_PASSWORD_GRANT_TYPE,
                    Permissions.Prefixes.GrantType + TokenController.LegacyRefreshGrantType,
                    .. OpenIddictEngineExtensions.Scopes.Select((scope) => Permissions.Prefixes.Scope + scope),
                ]);
                return descriptor;
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = services.CreateAsyncScope();
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var application = await applications.FindByClientIdAsync(Notesnook.ClientId!, cancellationToken);
            if (application == null) await applications.CreateAsync(Notesnook, cancellationToken);
            else await applications.UpdateAsync(application, Notesnook, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
