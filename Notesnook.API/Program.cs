using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Streetwriters.Common;

namespace Notesnook.API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
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
#if DEBUG
                        options.ListenAnyIP(int.Parse(Servers.NotesnookAPI.Port));
#else
                        options.ListenAnyIP(443, listenerOptions =>
                        {
                            listenerOptions.UseHttps(Servers.OriginSSLCertificate);
                        });
                        options.ListenAnyIP(80);
                        options.Listen(IPAddress.Parse(Servers.NotesnookAPI.Hostname), int.Parse(Servers.NotesnookAPI.Port));
#endif
                    });
                });
    }
}
