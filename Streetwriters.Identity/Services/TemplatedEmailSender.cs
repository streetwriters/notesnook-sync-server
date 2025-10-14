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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Cryptography;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Scriban;
using SendGrid;
using SendGrid.Helpers.Mail;
using Streetwriters.Common;
using Streetwriters.Common.Helpers;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;
using WebMarkupMin.Core;
using WebMarkupMin.Core.Loggers;

namespace Streetwriters.Identity.Services
{
    public class TemplatedEmailSender : ITemplatedEmailSender
    {
        NNGnuPGContext NNGnuPGContext { get; set; }
        IEmailSender EmailSender { get; set; }

        public TemplatedEmailSender(IConfiguration configuration, IEmailSender emailSender)
        {
            NNGnuPGContext = new NNGnuPGContext(configuration.GetSection("PgpKeySettings"));
            EmailSender = emailSender;
        }

        readonly EmailTemplate Email2FATemplate = new()
        {
            Html = HtmlHelper.ReadMinifiedHtmlFile("Templates/Email2FACode.html"),
            Text = File.ReadAllText("Templates/Email2FACode.txt"),
            Subject = "Your {{app_name}} account 2FA code",
        };

        readonly EmailTemplate ConfirmEmailTemplate = new()
        {
            Html = HtmlHelper.ReadMinifiedHtmlFile("Templates/ConfirmEmail.html"),
            Text = File.ReadAllText("Templates/ConfirmEmail.txt"),
            Subject = "Confirm your {{app_name}} account",
        };

        readonly EmailTemplate ConfirmChangeEmailTemplate = new()
        {
            Html = HtmlHelper.ReadMinifiedHtmlFile("Templates/EmailChangeConfirmation.html"),
            Text = File.ReadAllText("Templates/EmailChangeConfirmation.txt"),
            Subject = "Change {{app_name}} account email address",
        };

        readonly EmailTemplate PasswordResetEmailTemplate = new()
        {
            Html = HtmlHelper.ReadMinifiedHtmlFile("Templates/ResetAccountPassword.html"),
            Text = File.ReadAllText("Templates/ResetAccountPassword.txt"),
            Subject = "Reset {{app_name}} account password",
        };

        readonly EmailTemplate FailedLoginAlertTemplate = new()
        {
            Html = HtmlHelper.ReadMinifiedHtmlFile("Templates/FailedLoginAlert.html"),
            Text = File.ReadAllText("Templates/FailedLoginAlert.txt"),
            Subject = "Failed login attempt on your {{app_name}} account",
        };

        public async Task Send2FACodeEmailAsync(string email, string code, IClient client)
        {
            var template = new EmailTemplate()
            {
                Html = Email2FATemplate.Html,
                Text = Email2FATemplate.Text,
                Subject = Email2FATemplate.Subject,
                Data = new { app_name = client.Name, code },
            };
            await EmailSender.SendEmailAsync(email, template, client, NNGnuPGContext);
        }

        public async Task SendConfirmationEmailAsync(
            string email,
            string callbackUrl,
            IClient client
        )
        {
            var template = new EmailTemplate()
            {
                Html = ConfirmEmailTemplate.Html,
                Text = ConfirmEmailTemplate.Text,
                Subject = ConfirmEmailTemplate.Subject,
                Data = new { app_name = client.Name, confirm_link = callbackUrl },
            };
            await EmailSender.SendEmailAsync(email, template, client, NNGnuPGContext);
        }

        public async Task SendChangeEmailConfirmationAsync(
            string email,
            string code,
            IClient client
        )
        {
            var template = new EmailTemplate()
            {
                Html = ConfirmChangeEmailTemplate.Html,
                Text = ConfirmChangeEmailTemplate.Text,
                Subject = ConfirmChangeEmailTemplate.Subject,
                Data = new { app_name = client.Name, code },
            };
            await EmailSender.SendEmailAsync(email, template, client, NNGnuPGContext);
        }

        public async Task SendPasswordResetEmailAsync(
            string email,
            string callbackUrl,
            IClient client
        )
        {
            var template = new EmailTemplate()
            {
                Html = PasswordResetEmailTemplate.Html,
                Text = PasswordResetEmailTemplate.Text,
                Subject = PasswordResetEmailTemplate.Subject,
                Data = new { app_name = client.Name, reset_link = callbackUrl },
            };
            await EmailSender.SendEmailAsync(email, template, client, NNGnuPGContext);
        }

        public async Task SendFailedLoginAlertAsync(string email, string deviceInfo, IClient client)
        {
            var template = new EmailTemplate()
            {
                Html = FailedLoginAlertTemplate.Html,
                Text = FailedLoginAlertTemplate.Text,
                Subject = FailedLoginAlertTemplate.Subject,
                Data = new
                {
                    app_name = client.Name,
                    device_info = deviceInfo.Replace("\n", "<br>"),
                },
            };
            await EmailSender.SendEmailAsync(email, template, client, NNGnuPGContext);
        }
    }

    public class NNGnuPGContext(IConfiguration pgpKeySettings) : GnuPGContext
    {
        IConfiguration PgpKeySettings { get; set; } = pgpKeySettings;

        protected override string? GetPasswordForKey(PgpSecretKey key)
        {
            return PgpKeySettings[key.KeyId.ToString("X")];
        }
    }
}
