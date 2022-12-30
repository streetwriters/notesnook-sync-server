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

using System.Net.NetworkInformation;
using System.Net.Sockets;
#if !DEBUG
using System;
using System.Security.Cryptography.X509Certificates;
#endif

namespace Streetwriters.Common
{
    public class Server
    {
        public int Port { get; set; }
        public bool IsSecure { get; set; }
        public string Hostname { get; set; }
        public string Domain { get; set; }

        public override string ToString()
        {
            var url = "";
            url += IsSecure ? "https" : "http";
            url += $"://{Hostname}";
            url += IsSecure || Port == 80 ? "" : $":{Port}";
            return url;
        }

        public string WS()
        {
            var url = "";
            url += IsSecure ? "ws" : "ws";
            url += $"://{Hostname}";
            url += Port == 80 ? "" : $":{Port}";
            return url;
        }
    }

    public class Servers
    {
#if DEBUG
        public static string GetLocalIPv4(NetworkInterfaceType _type)
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            string output = "";
            foreach (NetworkInterface item in interfaces)
            {
                if (item.NetworkInterfaceType == _type && item.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            output = ip.Address.ToString();
                        }
                    }
                }
            }
            return output;
        }
        public readonly static string HOST = GetLocalIPv4(NetworkInterfaceType.Ethernet);
        public static Server S3Server { get; } = new()
        {
            Port = 4568,
            Hostname = HOST,
            IsSecure = false,
            Domain = HOST
        };
#else
        private readonly static string HOST = "localhost";
        public readonly static X509Certificate2 OriginSSLCertificate = string.IsNullOrEmpty(Constants.ORIGIN_CERT_PATH) || string.IsNullOrEmpty(Constants.ORIGIN_CERT_KEY_PATH) ? null : X509Certificate2.CreateFromPemFile(Constants.ORIGIN_CERT_PATH, Environment.GetEnvironmentVariable(Constants.ORIGIN_CERT_KEY_PATH));
#endif
        public static Server NotesnookAPI { get; } = new()
        {
            Domain = Constants.NOTESNOOK_SERVER_DOMAIN,
            Port = Constants.NOTESNOOK_SERVER_PORT,
            Hostname = Constants.NOTESNOOK_SERVER_HOST,
        };

        public static Server MessengerServer { get; } = new()
        {
            Domain = Constants.SSE_SERVER_DOMAIN,
            Port = Constants.SSE_SERVER_PORT,
            Hostname = Constants.SSE_SERVER_HOST,
        };

        public static Server IdentityServer { get; } = new()
        {
            Domain = Constants.IDENTITY_SERVER_DOMAIN,
            Port = Constants.IDENTITY_SERVER_PORT,
            Hostname = Constants.IDENTITY_SERVER_HOST,
        };

        public static Server SubscriptionServer { get; } = new()
        {
            Domain = "subscriptions.streetwriters.co",
        };
    }
}
