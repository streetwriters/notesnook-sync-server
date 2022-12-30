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

using Streetwriters.Identity.Interfaces;
using Streetwriters.Common.Interfaces;
using MessageBird;
using MessageBird.Objects;
using Microsoft.Extensions.Options;
using Streetwriters.Identity.Models;
using Streetwriters.Common;

namespace Streetwriters.Identity.Services
{
    public class SMSSender : ISMSSender
    {
        private Client client;
        public SMSSender()
        {
            if (!string.IsNullOrEmpty(Constants.MESSAGEBIRD_ACCESS_KEY))
                client = Client.CreateDefault(Constants.MESSAGEBIRD_ACCESS_KEY);
        }

        public string SendOTP(string number, IClient app)
        {
            VerifyOptionalArguments optionalArguments = new VerifyOptionalArguments
            {
                Originator = app.Name,
                Reference = app.Name,
                Type = MessageType.Sms,
                Template = $"Your {app.Name} 2FA code is: %token. Valid for 5 minutes.",
                TokenLength = 6,
                Timeout = 60 * 5
            };
            Verify verify = client.CreateVerify(number, optionalArguments);
            if (verify.Status == VerifyStatus.Sent) return verify.Id;
            return null;
        }

        public bool VerifyOTP(string id, string code)
        {
            Verify verify = client.SendVerifyToken(id, code);
            return verify.Status == VerifyStatus.Verified;
        }
    }
}