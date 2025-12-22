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

namespace Streetwriters.Common
{
    public class Constants
    {
        public static int COMPATIBILITY_VERSION = 1;
        public static bool IS_SELF_HOSTED => ReadSecret("SELF_HOSTED") == "1";
        public static bool DISABLE_SIGNUPS => ReadSecret("DISABLE_SIGNUPS") == "true";
        public static string INSTANCE_NAME => ReadSecret("INSTANCE_NAME") ?? "default";

        // S3 related
        public static string S3_ACCESS_KEY => ReadSecret("S3_ACCESS_KEY") ?? throw new InvalidOperationException("S3_ACCESS_KEY is required");
        public static string S3_ACCESS_KEY_ID => ReadSecret("S3_ACCESS_KEY_ID") ?? throw new InvalidOperationException("S3_ACCESS_KEY_ID is required");
        public static string S3_SERVICE_URL => ReadSecret("S3_SERVICE_URL") ?? throw new InvalidOperationException("S3_SERVICE_URL is required");
        public static string S3_REGION => ReadSecret("S3_REGION") ?? throw new InvalidOperationException("S3_REGION is required");
        public static string S3_BUCKET_NAME => ReadSecret("S3_BUCKET_NAME") ?? throw new InvalidOperationException("S3_BUCKET_NAME is required");
        public static string? S3_INTERNAL_BUCKET_NAME => ReadSecret("S3_INTERNAL_BUCKET_NAME");
        public static string? S3_INTERNAL_SERVICE_URL => ReadSecret("S3_INTERNAL_SERVICE_URL");

        // SMTP settings
        public static string? SMTP_USERNAME => ReadSecret("SMTP_USERNAME");
        public static string? SMTP_PASSWORD => ReadSecret("SMTP_PASSWORD");
        public static string? SMTP_HOST => ReadSecret("SMTP_HOST");
        public static string? SMTP_PORT => ReadSecret("SMTP_PORT");
        public static string? SMTP_REPLYTO_EMAIL => ReadSecret("SMTP_REPLYTO_EMAIL");
        public static string? NOTESNOOK_SENDER_EMAIL => ReadSecret("NOTESNOOK_SENDER_EMAIL") ?? ReadSecret("SMTP_USERNAME");

        public static string? NOTESNOOK_APP_HOST => ReadSecret("NOTESNOOK_APP_HOST");
        public static string NOTESNOOK_API_SECRET => ReadSecret("NOTESNOOK_API_SECRET") ?? throw new InvalidOperationException("NOTESNOOK_API_SECRET is required");

        // MessageBird is used for SMS sending
        public static string? TWILIO_ACCOUNT_SID => ReadSecret("TWILIO_ACCOUNT_SID");
        public static string? TWILIO_AUTH_TOKEN => ReadSecret("TWILIO_AUTH_TOKEN");
        public static string? TWILIO_SERVICE_SID => ReadSecret("TWILIO_SERVICE_SID");
        // Server discovery
        public static int NOTESNOOK_SERVER_PORT => int.Parse(ReadSecret("NOTESNOOK_SERVER_PORT") ?? "80");
        public static string? NOTESNOOK_SERVER_HOST => ReadSecret("NOTESNOOK_SERVER_HOST");
        public static string? NOTESNOOK_CERT_PATH => ReadSecret("NOTESNOOK_CERT_PATH");
        public static string? NOTESNOOK_CERT_KEY_PATH => ReadSecret("NOTESNOOK_CERT_KEY_PATH");
        public static string[] KNOWN_PROXIES => (ReadSecret("KNOWN_PROXIES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public static int IDENTITY_SERVER_PORT => int.Parse(ReadSecret("IDENTITY_SERVER_PORT") ?? "80");
        public static string? IDENTITY_SERVER_HOST => ReadSecret("IDENTITY_SERVER_HOST");
        public static Uri? IDENTITY_SERVER_URL => ReadSecret("IDENTITY_SERVER_URL") is string url ? new Uri(url) : null;
        public static string? IDENTITY_CERT_PATH => ReadSecret("IDENTITY_CERT_PATH");
        public static string? IDENTITY_CERT_KEY_PATH => ReadSecret("IDENTITY_CERT_KEY_PATH");

        public static int SSE_SERVER_PORT => int.Parse(ReadSecret("SSE_SERVER_PORT") ?? "80");
        public static string? SSE_SERVER_HOST => ReadSecret("SSE_SERVER_HOST");
        public static string? SSE_CERT_PATH => ReadSecret("SSE_CERT_PATH");
        public static string? SSE_CERT_KEY_PATH => ReadSecret("SSE_CERT_KEY_PATH");

        // internal
        public static string? WEBRISK_API_URI => ReadSecret("WEBRISK_API_URI");
        public static string MONGODB_CONNECTION_STRING => ReadSecret("MONGODB_CONNECTION_STRING") ?? throw new ArgumentNullException("MONGODB_CONNECTION_STRING environment variable is not set");
        public static string MONGODB_DATABASE_NAME => ReadSecret("MONGODB_DATABASE_NAME") ?? throw new ArgumentNullException("MONGODB_DATABASE_NAME environment variable is not set");
        public static int SUBSCRIPTIONS_SERVER_PORT => int.Parse(ReadSecret("SUBSCRIPTIONS_SERVER_PORT") ?? "80");
        public static string? SUBSCRIPTIONS_SERVER_HOST => ReadSecret("SUBSCRIPTIONS_SERVER_HOST");
        public static string? SUBSCRIPTIONS_CERT_PATH => ReadSecret("SUBSCRIPTIONS_CERT_PATH");
        public static string? SUBSCRIPTIONS_CERT_KEY_PATH => ReadSecret("SUBSCRIPTIONS_CERT_KEY_PATH");
        public static string[] NOTESNOOK_CORS_ORIGINS => ReadSecret("NOTESNOOK_CORS")?.Split(",") ?? [];
        public static string MONOGRAPH_PUBLIC_URL => ReadSecret("MONOGRAPH_PUBLIC_URL") ?? "https://monogr.phf";

        public static string? ReadSecret(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) return value;
            var file = Environment.GetEnvironmentVariable(name + "_FILE");
            if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file))
            {
                return System.IO.File.ReadAllText(file);
            }
            return null;
        }
    }
}

