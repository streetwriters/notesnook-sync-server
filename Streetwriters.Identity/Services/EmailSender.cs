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

using System.Threading.Tasks;
using Streetwriters.Identity.Interfaces;
using SendGrid;
using SendGrid.Helpers.Mail;
using Streetwriters.Common;
using Streetwriters.Common.Interfaces;
using Streetwriters.Identity.Models;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using MailKit.Net.Smtp;
using MailKit;
using MimeKit;
using System.IO;
using Scriban;
using WebMarkupMin.Core;
using WebMarkupMin.Core.Loggers;
using MimeKit.Cryptography;
using Org.BouncyCastle.Bcpg.OpenPgp;
using System.Linq;
using System.Threading;
using Org.BouncyCastle.Bcpg;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Streetwriters.Identity.Services
{
    public class EmailSender : IEmailSender, IAsyncDisposable
    {
        NNGnuPGContext NNGnuPGContext { get; set; }
        SmtpClient mailClient;
        public EmailSender(IConfiguration configuration)
        {
            NNGnuPGContext = new NNGnuPGContext(configuration.GetSection("PgpKeySettings"));
            mailClient = new SmtpClient();
        }

        EmailTemplate Email2FATemplate = new EmailTemplate
        {
            Html = ReadMinifiedHtmlFile("Templates/Email2FACode.html"),
            Text = File.ReadAllText("Templates/Email2FACode.txt"),
            Subject = "Your {{app_name}} account 2FA code",
        };

        EmailTemplate ConfirmEmailTemplate = new EmailTemplate
        {
            Html = ReadMinifiedHtmlFile("Templates/ConfirmEmail.html"),
            Text = File.ReadAllText("Templates/ConfirmEmail.txt"),
            Subject = "Confirm your {{app_name}} account",
        };

        EmailTemplate ConfirmChangeEmailTemplate = new EmailTemplate
        {
            Html = ReadMinifiedHtmlFile("Templates/EmailChangeConfirmation.html"),
            Text = File.ReadAllText("Templates/EmailChangeConfirmation.txt"),
            Subject = "Change {{app_name}} account email address",
        };

        EmailTemplate PasswordResetEmailTemplate = new EmailTemplate
        {
            Html = ReadMinifiedHtmlFile("Templates/ResetAccountPassword.html"),
            Text = File.ReadAllText("Templates/ResetAccountPassword.txt"),
            Subject = "Reset {{app_name}} account password",
        };

        EmailTemplate FailedLoginAlertTemplate = new EmailTemplate
        {
            Html = ReadMinifiedHtmlFile("Templates/FailedLoginAlert.html"),
            Text = File.ReadAllText("Templates/FailedLoginAlert.txt"),
            Subject = "Failed login attempt on your {{app_name}} account",
        };

        public async Task Send2FACodeEmailAsync(string email, string code, IClient client)
        {
            var template = new EmailTemplate
            {
                Html = Email2FATemplate.Html,
                Text = Email2FATemplate.Text,
                Subject = Email2FATemplate.Subject,
                Data = new
                {
                    app_name = client.Name,
                    code = code
                }
            };
            await SendEmailAsync(email, template, client);
        }

        public async Task SendConfirmationEmailAsync(string email, string callbackUrl, IClient client)
        {
            var template = new EmailTemplate
            {
                Html = ConfirmEmailTemplate.Html,
                Text = ConfirmEmailTemplate.Text,
                Subject = ConfirmEmailTemplate.Subject,
                Data = new
                {
                    app_name = client.Name,
                    confirm_link = callbackUrl
                }
            };
            await SendEmailAsync(email, template, client);
        }

        public async Task SendChangeEmailConfirmationAsync(string email, string code, IClient client)
        {
            var template = new EmailTemplate
            {
                Html = ConfirmChangeEmailTemplate.Html,
                Text = ConfirmChangeEmailTemplate.Text,
                Subject = ConfirmChangeEmailTemplate.Subject,
                Data = new
                {
                    app_name = client.Name,
                    code = code
                }
            };
            await SendEmailAsync(email, template, client);
        }

        public async Task SendPasswordResetEmailAsync(string email, string callbackUrl, IClient client)
        {
            var template = new EmailTemplate
            {
                Html = PasswordResetEmailTemplate.Html,
                Text = PasswordResetEmailTemplate.Text,
                Subject = PasswordResetEmailTemplate.Subject,
                Data = new
                {
                    app_name = client.Name,
                    reset_link = callbackUrl
                }
            };
            await SendEmailAsync(email, template, client);
        }


        public async Task SendFailedLoginAlertAsync(string email, string deviceInfo, IClient client)
        {
            var template = new EmailTemplate
            {
                Html = FailedLoginAlertTemplate.Html,
                Text = FailedLoginAlertTemplate.Text,
                Subject = FailedLoginAlertTemplate.Subject,
                Data = new
                {
                    app_name = client.Name,
                    device_info = deviceInfo.Replace("\n", "<br>")
                }
            };
            await SendEmailAsync(email, template, client);
        }

        private async Task SendEmailAsync(string email, IEmailTemplate template, IClient client)
        {
            if (!mailClient.IsConnected)
            {
                if (int.TryParse(Constants.SMTP_PORT, out int port))
                {
                    await mailClient.ConnectAsync(Constants.SMTP_HOST, port, MailKit.Security.SecureSocketOptions.StartTls);
                }
                else
                {
                    throw new InvalidDataException("SMTP_PORT is not a valid integer value.");
                }
            }

            if (!mailClient.IsAuthenticated)
                await mailClient.AuthenticateAsync(Constants.SMTP_USERNAME, Constants.SMTP_PASSWORD);

            var message = new MimeMessage();
            var sender = new MailboxAddress(client.SenderName, client.SenderEmail);
            message.From.Add(sender);
            message.To.Add(new MailboxAddress("", email));
            message.Subject = await Template.Parse(template.Subject).RenderAsync(template.Data);

            if (!string.IsNullOrEmpty(Constants.SMTP_REPLYTO_NAME) && !string.IsNullOrEmpty(Constants.SMTP_REPLYTO_EMAIL))
                message.ReplyTo.Add(new MailboxAddress(Constants.SMTP_REPLYTO_NAME, Constants.SMTP_REPLYTO_EMAIL));

            message.Body = await GetEmailBodyAsync(template, client, sender);

            await mailClient.SendAsync(message);
        }

        private async Task<MimeEntity> GetEmailBodyAsync(IEmailTemplate template, IClient client, MailboxAddress sender)
        {
            var builder = new BodyBuilder();
            try
            {
                builder.TextBody = await Template.Parse(template.Text).RenderAsync(template.Data);
                builder.HtmlBody = await Template.Parse(template.Html).RenderAsync(template.Data);

                var key = NNGnuPGContext.GetSigningKey(sender);
                if (key != null)
                {
                    using (MemoryStream outputStream = new MemoryStream())
                    {
                        using (Stream armoredStream = new ArmoredOutputStream(outputStream))
                        {
                            key.PublicKey.Encode(armoredStream);
                        }
                        outputStream.Seek(0, SeekOrigin.Begin);
                        builder.Attachments.Add($"{client.Id}_pub.asc", Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(outputStream.ToArray())));
                    }
                    return await MultipartSigned.CreateAsync(NNGnuPGContext, sender, DigestAlgorithm.Sha256, builder.ToMessageBody());
                }
                else
                {
                    return builder.ToMessageBody();
                }
            }
            catch (PrivateKeyNotFoundException)
            {
                return builder.ToMessageBody();
            }
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await mailClient.DisconnectAsync(true);
            mailClient.Dispose();
        }


        static string ReadMinifiedHtmlFile(string path)
        {
            var settings = new HtmlMinificationSettings()
            {
                WhitespaceMinificationMode = WhitespaceMinificationMode.Medium
            };
            var cssMinifier = new KristensenCssMinifier();
            var jsMinifier = new CrockfordJsMinifier();

            var minifier = new HtmlMinifier(settings, cssMinifier, jsMinifier, new NullLogger());

            return minifier.Minify(File.ReadAllText(path), false).MinifiedContent;
        }
    }

    class NNGnuPGContext : GnuPGContext
    {
        IConfiguration PgpKeySettings { get; set; }
        public NNGnuPGContext(IConfiguration pgpKeySettings)
        {
            PgpKeySettings = pgpKeySettings;
        }

        protected override string GetPasswordForKey(PgpSecretKey key)
        {
            return PgpKeySettings[key.KeyId.ToString("X")];
        }
    }
}