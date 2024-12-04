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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Streetwriters.Common;

namespace Streetwriters.Identity
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
#if (DEBUG || STAGING)
            DotNetEnv.Env.TraversePath().Load(".env.local");
#else
            DotNetEnv.Env.TraversePath().Load(".env");
#endif
            IHost host = CreateHostBuilder(args).Build();
            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureLogging((options) =>
            {
                options.AddConsole();
                options.AddSimpleConsole();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .UseStartup<Startup>()
                    .UseKestrel((options) =>
                    {
                        options.Limits.MaxRequestBodySize = long.MaxValue;
                        options.ListenAnyIP(Servers.IdentityServer.Port);
                        if (Servers.IdentityServer.IsSecure)
                        {
                            options.ListenAnyIP(443, listenerOptions =>
                            {
                                listenerOptions.UseHttps(Servers.IdentityServer.SSLCertificate);
                            });
                        }
                    });
            });
    }
}
