using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Streetwriters.Common
{
    public class Server
    {
        public string Port { get; set; }
        public bool IsSecure { get; set; }
        public string Hostname { get; set; }
        public string Domain { get; set; }

        public override string ToString()
        {
            var url = "";
            url += IsSecure ? "https" : "http";
            url += $"://{Hostname}";
            url += IsSecure ? "" : $":{Port}";
            return url;
        }

        public string WS()
        {
            var url = "";
            url += IsSecure ? "ws" : "ws";
            url += $"://{Hostname}";
            url += $":{Port}";
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
            Port = "4568",
            Hostname = HOST,
            IsSecure = false,
            Domain = HOST
        };
#else
        private readonly static string HOST = "localhost";
        public readonly static X509Certificate2 OriginSSLCertificate = X509Certificate2.CreateFromPemFile("/home/notesnook/.ssl/CF_Origin_Streetwriters.pem", "/home/notesnook/.ssl/CF_Origin_Streetwriters.key");
#endif
        public static Server NotesnookAPI { get; } = new()
        {
            Domain = "api.notesnook.com",
            Port = "5264",
#if DEBUG
            IsSecure = false,
            Hostname = HOST,
#else
            IsSecure = true,
            Hostname = "10.0.0.5",
#endif
        };

        public static Server MessengerServer { get; } = new()
        {
            Domain = "events.streetwriters.co",
            Port = "7264",
#if DEBUG
            IsSecure = false,
            Hostname = HOST,
#else
            IsSecure = true,
            Hostname = "10.0.0.6",
#endif
        };

        public static Server IdentityServer { get; } = new()
        {
            Domain = "auth.streetwriters.co",
            IsSecure = false,
            Port = "8264",
#if DEBUG
            Hostname = HOST,
#else
            Hostname = "10.0.0.4",
#endif
        };

        public static Server SubscriptionServer { get; } = new()
        {
            Domain = "subscriptions.streetwriters.co",
            IsSecure = false,
            Port = "9264",
#if DEBUG
            Hostname = HOST,
#else
            Hostname = "10.0.0.4",
#endif
        };
        public static Server PaymentsServer { get; } = new()
        {
            Domain = "payments.streetwriters.co",
            IsSecure = false,
            Port = "6264",
#if DEBUG
            Hostname = HOST,
#else
            Hostname = "10.0.0.4",
#endif
        };
    }
}
