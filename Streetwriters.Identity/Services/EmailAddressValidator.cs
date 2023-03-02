using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Streetwriters.Common;

namespace Streetwriters.Identity.Services
{
    public class EmailAddressValidator
    {
        private static DateTimeOffset LAST_FETCH_TIME = DateTimeOffset.MinValue;
        private static HashSet<string> BLACKLISTED_DOMAINS = new HashSet<string>();

        public static async Task<bool> IsEmailAddressValidAsync(string email)
        {
            var domain = email.ToLowerInvariant().Split("@")[1];
            try
            {
                if (LAST_FETCH_TIME.AddDays(1) < DateTimeOffset.UtcNow)
                {
                    var httpClient = new HttpClient();
                    var domainsList = await httpClient.GetStringAsync("https://disposable.github.io/disposable-email-domains/domains.txt");
                    BLACKLISTED_DOMAINS = new HashSet<string>(domainsList.Split('\n'));
                    LAST_FETCH_TIME = DateTimeOffset.UtcNow;
                }

                return !BLACKLISTED_DOMAINS.Contains(domain);
            }
            catch (Exception ex)
            {
                await Slogger<EmailAddressValidator>.Error("IsEmailAddressValidAsync", ex.ToString());
                return BLACKLISTED_DOMAINS.Count > 0 ? !BLACKLISTED_DOMAINS.Contains(domain) : true;
            }
        }
    }
}