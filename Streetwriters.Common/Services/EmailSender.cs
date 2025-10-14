using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Cryptography;
using Org.BouncyCastle.Bcpg;
using Scriban;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;

namespace Streetwriters.Common.Services
{
    public class EmailSender : IEmailSender, IAsyncDisposable
    {
        private readonly SmtpClient mailClient = new();
        private readonly ILogger<EmailSender> logger;

        public EmailSender(ILogger<EmailSender> logger)
        {
            this.logger = logger;
        }

        public async Task SendEmailAsync(
            string email,
            EmailTemplate template,
            IClient client,
            GnuPGContext? gpgContext = null,
            Dictionary<string, byte[]>? attachments = null
        )
        {
            if (!mailClient.IsConnected)
            {
                if (int.TryParse(Common.Constants.SMTP_PORT, out int port))
                {
                    await mailClient.ConnectAsync(
                        Common.Constants.SMTP_HOST,
                        port,
                        MailKit.Security.SecureSocketOptions.Auto
                    );
                }
                else
                {
                    throw new InvalidDataException("SMTP_PORT is not a valid integer value.");
                }
            }

            if (!mailClient.IsAuthenticated)
                await mailClient.AuthenticateAsync(
                    Common.Constants.SMTP_USERNAME,
                    Common.Constants.SMTP_PASSWORD
                );

            var message = new MimeMessage();
            var sender = new MailboxAddress(client.SenderName, client.SenderEmail);
            message.From.Add(sender);
            message.To.Add(new MailboxAddress("", email));
            message.Subject = await Template.Parse(template.Subject).RenderAsync(template.Data);

            if (!string.IsNullOrEmpty(Common.Constants.SMTP_REPLYTO_EMAIL))
                message.ReplyTo.Add(MailboxAddress.Parse(Common.Constants.SMTP_REPLYTO_EMAIL));

            message.Body = await GetEmailBodyAsync(
                template,
                client,
                sender,
                gpgContext,
                attachments
            );

            await mailClient.SendAsync(message);
        }

        private async Task<MimeEntity> GetEmailBodyAsync(
            EmailTemplate template,
            IClient client,
            MailboxAddress sender,
            GnuPGContext? gpgContext = null,
            Dictionary<string, byte[]>? attachments = null
        )
        {
            var builder = new BodyBuilder();
            try
            {
                builder.TextBody = await Template.Parse(template.Text).RenderAsync(template.Data);
                builder.HtmlBody = await Template.Parse(template.Html).RenderAsync(template.Data);

                if (attachments != null)
                {
                    foreach (var attachment in attachments)
                    {
                        builder.Attachments.Add(attachment.Key, attachment.Value);
                    }
                }

                var key = gpgContext?.GetSigningKey(sender);
                if (key != null)
                {
                    using (MemoryStream outputStream = new())
                    {
                        using (Stream armoredStream = new ArmoredOutputStream(outputStream))
                        {
                            key.PublicKey.Encode(armoredStream);
                        }
                        outputStream.Seek(0, SeekOrigin.Begin);
                        builder.Attachments.Add(
                            $"{client.Id}_pub.asc",
                            Encoding.ASCII.GetBytes(
                                Encoding.ASCII.GetString(outputStream.ToArray())
                            )
                        );
                    }
                    return await MultipartSigned.CreateAsync(
                        gpgContext,
                        sender,
                        DigestAlgorithm.Sha256,
                        builder.ToMessageBody()
                    );
                }
                else
                {
                    return builder.ToMessageBody();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get email body");
                return builder.ToMessageBody();
            }
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await mailClient.DisconnectAsync(true);
            mailClient.Dispose();
        }
    }
}
