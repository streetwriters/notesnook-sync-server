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
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Streetwriters.Common;

namespace Streetwriters.Messenger
{
    public class Program
    {
        public static void Main(string[] args)
        {
#if DEBUG
            DotNetEnv.Env.TraversePath().Load(".env.local");
#else
            DotNetEnv.Env.TraversePath().Load(".env");
#endif
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>()
                    .UseKestrel((options) =>
                    {
                        options.Limits.MaxRequestBodySize = long.MaxValue;
                        options.ListenAnyIP(Servers.MessengerServer.Port);
                        if (Servers.MessengerServer.IsSecure)
                        {
                            options.ListenAnyIP(443, listenerOptions =>
                            {
                                listenerOptions.UseHttps(Servers.MessengerServer.SSLCertificate);
                            });
                        }
                    });
                });
    }
}
