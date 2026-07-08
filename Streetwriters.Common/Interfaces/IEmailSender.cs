using System.Collections.Generic;
using System.Net.Mail;
using System.Threading.Tasks;
using MimeKit;
using MimeKit.Cryptography;
using Streetwriters.Common.Models;

namespace Streetwriters.Common.Interfaces
{
    public interface IEmailSender
    {
        Task SendEmailAsync(
            string email,
            EmailTemplate template,
            MailAddress from,
            GnuPGContext? gpgContext = null,
            Dictionary<string, byte[]>? attachments = null
        );
    }
}
