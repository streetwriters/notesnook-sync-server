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

#if !DEBUG
using System.Net;
#endif
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Streetwriters.Common;
using System.Linq;

namespace Notesnook.API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
#if DEBUG
            DotNetEnv.Env.TraversePath().Load(".env.local");
#else
            DotNetEnv.Env.TraversePath().Load(".env");
#endif
            IHost host = CreateHostBuilder(args).Build();
            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                    .UseStartup<Startup>()
                    .UseKestrel((options) =>
                    {
                        options.Limits.MaxRequestBodySize = long.MaxValue;
                        options.ListenAnyIP(Servers.NotesnookAPI.Port);
                        if (Servers.NotesnookAPI.IsSecure)
                        {
                            options.ListenAnyIP(443, listenerOptions =>
                            {
                                listenerOptions.UseHttps(Servers.NotesnookAPI.SSLCertificate);
                            });
                        }
                    });
                });
    }
}
