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

using Streetwriters.Identity.Interfaces;
using Streetwriters.Common.Interfaces;
using MessageBird;
using MessageBird.Objects;
using Microsoft.Extensions.Options;
using Streetwriters.Identity.Models;
using Streetwriters.Common;
using Twilio.Rest.Verify.V2.Service;
using Twilio;
using System.Threading.Tasks;
using System;

namespace Streetwriters.Identity.Services
{
    public class SMSSender : ISMSSender
    {
        private Client client;
        public SMSSender()
        {
            if (!string.IsNullOrEmpty(Constants.MESSAGEBIRD_ACCESS_KEY))
                client = Client.CreateDefault(Constants.MESSAGEBIRD_ACCESS_KEY);


            if (!string.IsNullOrEmpty(Constants.TWILIO_ACCOUNT_SID) && !string.IsNullOrEmpty(Constants.TWILIO_AUTH_TOKEN))
            {
                TwilioClient.Init(Constants.TWILIO_ACCOUNT_SID, Constants.TWILIO_AUTH_TOKEN);
            }
        }

        public async Task<string> SendOTPAsync(string number, IClient app)
        {
            try
            {
                var verification = await VerificationResource.CreateAsync(
                    to: number,
                    channel: "sms",
                    pathServiceSid: Constants.TWILIO_SERVICE_SID
                );
                return verification.Sid;
            }
            catch (Exception ex)
            {

            }
            return null;
        }

        public async Task<bool> VerifyOTPAsync(string id, string code)
        {
            return (await VerificationCheckResource.CreateAsync(
                verificationSid: id,
                pathServiceSid: Constants.TWILIO_SERVICE_SID,
                code: code
            )).Status == "approved";
        }
    }
}