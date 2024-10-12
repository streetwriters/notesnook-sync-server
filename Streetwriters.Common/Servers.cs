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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
#if !DEBUG
using System;
using System.Security.Cryptography.X509Certificates;
#endif

namespace Streetwriters.Common
{
    public class Server
    {
        public Server(string originCertPath = null, string originCertKeyPath = null)
        {
            if (!string.IsNullOrEmpty(originCertPath) && !string.IsNullOrEmpty(originCertKeyPath))
                this.SSLCertificate = X509Certificate2.CreateFromPemFile(originCertPath, originCertKeyPath);
        }
        public string Id { get; set; }
        public int Port { get; set; }
        public string Hostname { get; set; }
        public Uri PublicURL { get; set; }
        public X509Certificate2 SSLCertificate { get; }
        public bool IsSecure { get => this.SSLCertificate != null; }

        public override string ToString()
        {
            var url = "";
            url += "http";
            url += $"://{Hostname}";
            url += Port == 80 || Port == 443 ? "" : $":{Port}";
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
        public static string GetLocalIPv4()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            string output = "";
            foreach (NetworkInterface item in interfaces)
            {
                if ((item.NetworkInterfaceType == NetworkInterfaceType.Ethernet || item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) && item.OperationalStatus == OperationalStatus.Up)
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
        public readonly static string HOST = GetLocalIPv4();
        public static Server S3Server { get; } = new()
        {
            Port = 4568,
            Hostname = HOST
        };
#endif
        public static Server NotesnookAPI { get; } = new(Constants.NOTESNOOK_CERT_PATH, Constants.NOTESNOOK_CERT_KEY_PATH)
        {
            Port = Constants.NOTESNOOK_SERVER_PORT,
            Hostname = Constants.NOTESNOOK_SERVER_HOST,
            Id = "notesnook-sync"
        };

        public static Server MessengerServer { get; } = new(Constants.SSE_CERT_PATH, Constants.SSE_CERT_KEY_PATH)
        {
            Port = Constants.SSE_SERVER_PORT,
            Hostname = Constants.SSE_SERVER_HOST,
            Id = "sse"
        };

        public static Server IdentityServer { get; } = new(Constants.IDENTITY_CERT_PATH, Constants.IDENTITY_CERT_KEY_PATH)
        {
            PublicURL = Constants.IDENTITY_SERVER_URL,
            Port = Constants.IDENTITY_SERVER_PORT,
            Hostname = Constants.IDENTITY_SERVER_HOST,
            Id = "auth"
        };

        public static Server SubscriptionServer { get; } = new(Constants.SUBSCRIPTIONS_CERT_PATH, Constants.SUBSCRIPTIONS_CERT_KEY_PATH)
        {
            Port = Constants.SUBSCRIPTIONS_SERVER_PORT,
            Hostname = Constants.SUBSCRIPTIONS_SERVER_HOST,
            Id = "subscription"
        };
    }
}
