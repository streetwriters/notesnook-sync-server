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
        public static bool IS_SELF_HOSTED => Environment.GetEnvironmentVariable("SELF_HOSTED") == "1";
        public static bool DISABLE_SIGNUPS => Environment.GetEnvironmentVariable("DISABLE_SIGNUPS") == "true";
        public static string INSTANCE_NAME => Environment.GetEnvironmentVariable("INSTANCE_NAME") ?? "default";

        // S3 related
        public static string S3_ACCESS_KEY => Environment.GetEnvironmentVariable("S3_ACCESS_KEY") ?? throw new InvalidOperationException("S3_ACCESS_KEY is required");
        public static string S3_ACCESS_KEY_ID => Environment.GetEnvironmentVariable("S3_ACCESS_KEY_ID") ?? throw new InvalidOperationException("S3_ACCESS_KEY_ID is required");
        public static string S3_SERVICE_URL => Environment.GetEnvironmentVariable("S3_SERVICE_URL") ?? throw new InvalidOperationException("S3_SERVICE_URL is required");
        public static string S3_REGION => Environment.GetEnvironmentVariable("S3_REGION") ?? throw new InvalidOperationException("S3_REGION is required");
        public static string S3_BUCKET_NAME => Environment.GetEnvironmentVariable("S3_BUCKET_NAME") ?? throw new InvalidOperationException("S3_BUCKET_NAME is required");
        public static string? S3_INTERNAL_BUCKET_NAME => Environment.GetEnvironmentVariable("S3_INTERNAL_BUCKET_NAME");
        public static string? S3_INTERNAL_SERVICE_URL => Environment.GetEnvironmentVariable("S3_INTERNAL_SERVICE_URL");

        // SMTP settings
        public static string? SMTP_USERNAME => Environment.GetEnvironmentVariable("SMTP_USERNAME");
        public static string? SMTP_PASSWORD => Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        public static string? SMTP_HOST => Environment.GetEnvironmentVariable("SMTP_HOST");
        public static string? SMTP_PORT => Environment.GetEnvironmentVariable("SMTP_PORT");
        public static string? SMTP_REPLYTO_EMAIL => Environment.GetEnvironmentVariable("SMTP_REPLYTO_EMAIL");
        public static string? NOTESNOOK_SENDER_EMAIL => Environment.GetEnvironmentVariable("NOTESNOOK_SENDER_EMAIL") ?? Environment.GetEnvironmentVariable("SMTP_USERNAME");

        public static string? NOTESNOOK_APP_HOST => Environment.GetEnvironmentVariable("NOTESNOOK_APP_HOST");
        public static string NOTESNOOK_API_SECRET => Environment.GetEnvironmentVariable("NOTESNOOK_API_SECRET") ?? throw new InvalidOperationException("NOTESNOOK_API_SECRET is required");

        // MessageBird is used for SMS sending
        public static string? TWILIO_ACCOUNT_SID => Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID");
        public static string? TWILIO_AUTH_TOKEN => Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN");
        public static string? TWILIO_SERVICE_SID => Environment.GetEnvironmentVariable("TWILIO_SERVICE_SID");
        // Server discovery
        public static int NOTESNOOK_SERVER_PORT => int.Parse(Environment.GetEnvironmentVariable("NOTESNOOK_SERVER_PORT") ?? "80");
        public static string? NOTESNOOK_SERVER_HOST => Environment.GetEnvironmentVariable("NOTESNOOK_SERVER_HOST");
        public static string? NOTESNOOK_CERT_PATH => Environment.GetEnvironmentVariable("NOTESNOOK_CERT_PATH");
        public static string? NOTESNOOK_CERT_KEY_PATH => Environment.GetEnvironmentVariable("NOTESNOOK_CERT_KEY_PATH");
        public static string[] KNOWN_PROXIES => (Environment.GetEnvironmentVariable("KNOWN_PROXIES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public static int IDENTITY_SERVER_PORT => int.Parse(Environment.GetEnvironmentVariable("IDENTITY_SERVER_PORT") ?? "80");
        public static string? IDENTITY_SERVER_HOST => Environment.GetEnvironmentVariable("IDENTITY_SERVER_HOST");
        public static Uri? IDENTITY_SERVER_URL => Environment.GetEnvironmentVariable("IDENTITY_SERVER_URL") is string url ? new Uri(url) : null;
        public static string? IDENTITY_CERT_PATH => Environment.GetEnvironmentVariable("IDENTITY_CERT_PATH");
        public static string? IDENTITY_CERT_KEY_PATH => Environment.GetEnvironmentVariable("IDENTITY_CERT_KEY_PATH");

        public static int SSE_SERVER_PORT => int.Parse(Environment.GetEnvironmentVariable("SSE_SERVER_PORT") ?? "80");
        public static string? SSE_SERVER_HOST => Environment.GetEnvironmentVariable("SSE_SERVER_HOST");
        public static string? SSE_CERT_PATH => Environment.GetEnvironmentVariable("SSE_CERT_PATH");
        public static string? SSE_CERT_KEY_PATH => Environment.GetEnvironmentVariable("SSE_CERT_KEY_PATH");

        // internal
        public static string? WEBRISK_API_URI => Environment.GetEnvironmentVariable("WEBRISK_API_URI");
        public static string MONGODB_CONNECTION_STRING => Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING") ?? throw new ArgumentNullException("MONGODB_CONNECTION_STRING environment variable is not set");
        public static string MONGODB_DATABASE_NAME => Environment.GetEnvironmentVariable("MONGODB_DATABASE_NAME") ?? throw new ArgumentNullException("MONGODB_DATABASE_NAME environment variable is not set");
        public static int SUBSCRIPTIONS_SERVER_PORT => int.Parse(Environment.GetEnvironmentVariable("SUBSCRIPTIONS_SERVER_PORT") ?? "80");
        public static string? SUBSCRIPTIONS_SERVER_HOST => Environment.GetEnvironmentVariable("SUBSCRIPTIONS_SERVER_HOST");
        public static string? SUBSCRIPTIONS_CERT_PATH => Environment.GetEnvironmentVariable("SUBSCRIPTIONS_CERT_PATH");
        public static string? SUBSCRIPTIONS_CERT_KEY_PATH => Environment.GetEnvironmentVariable("SUBSCRIPTIONS_CERT_KEY_PATH");
        public static string[] NOTESNOOK_CORS_ORIGINS => Environment.GetEnvironmentVariable("NOTESNOOK_CORS")?.Split(",") ?? new string[] { };
    }
}

