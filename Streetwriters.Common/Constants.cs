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

using System;

namespace Streetwriters.Common
{
    public class Constants
    {
        // S3 related
        public static string S3_ACCESS_KEY = Environment.GetEnvironmentVariable("S3_ACCESS_KEY");
        public static string S3_ACCESS_KEY_ID = Environment.GetEnvironmentVariable("S3_ACCESS_KEY_ID");
        public static string S3_SERVICE_URL = Environment.GetEnvironmentVariable("S3_HOST");
        public static string S3_REGION = Environment.GetEnvironmentVariable("S3_HOST");

        // SMTP settings
        public static string SMTP_USERNAME = Environment.GetEnvironmentVariable("SMTP_USERNAME");
        public static string SMTP_PASSWORD = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        public static string SMTP_HOST = Environment.GetEnvironmentVariable("SMTP_HOST");
        public static string SMTP_PORT = Environment.GetEnvironmentVariable("SMTP_PORT");
        public static string SMTP_REPLYTO_NAME = Environment.GetEnvironmentVariable("SMTP_REPLYTO_NAME");
        public static string SMTP_REPLYTO_EMAIL = Environment.GetEnvironmentVariable("SMTP_REPLYTO_EMAIL");
        public static string NOTESNOOK_SENDER_EMAIL = Environment.GetEnvironmentVariable("NOTESNOOK_SENDER_EMAIL");
        public static string NOTESNOOK_SENDER_NAME = Environment.GetEnvironmentVariable("NOTESNOOK_SENDER_NAME");

        public static string NOTESNOOK_API_SECRET = Environment.GetEnvironmentVariable("NOTESNOOK_API_SECRET");

        // MessageBird is used for SMS sending
        public static string MESSAGEBIRD_ACCESS_KEY = Environment.GetEnvironmentVariable("MESSAGEBIRD_ACCESS_KEY");
    }
}