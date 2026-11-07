using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Identity;
using MimeKit;

namespace Streetwriters.Identity.Services
{
    public static class EmailCanonicalizer
    {
        private static readonly ParserOptions StrictParser = new()
        {
            AddressParserComplianceMode = RfcComplianceMode.Strict,
            AllowAddressesWithoutDomain = false,
            AllowUnquotedCommasInAddresses = false,
            Rfc2047ComplianceMode = RfcComplianceMode.Strict
        };
        private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = true, AllowUnassigned = false };
        private const string LOCAL_PART_SYMBOLS = "!#$%&'*+/=?^_`{|}~-";

        /// <param name="canonical">The address with its domain in lowercase ASCII (punycode for international domains).</param>
        public static bool TryCanonicalize(string? email, out string canonical, out string domain)
        {
            canonical = domain = string.Empty;
            if (string.IsNullOrEmpty(email) || email.Length > 254) return false;

            // full-width and other compatibility forms of ASCII characters become plain ASCII first
            var text = email.Normalize(NormalizationForm.FormKC);
            if (text.IndexOfAny(['<', '>', '(', ')', '"', '[', ']', ',', ';', ':', ' ', '\t', '\r', '\n']) >= 0) return false;
            if (!MailboxAddress.TryParse(StrictParser, text, out var mailbox) || mailbox.Route.Count > 0) return false;

            var local = mailbox.LocalPart;
            var rawDomain = mailbox.Domain;
            if (local + "@" + rawDomain != text) return false;
            // ASCII only: almost no provider accepts others, and they allow look-alikes of other people's addresses
            if (local.Length > 64 || !IsDotAtom(local, (c) => char.IsAsciiLetterOrDigit(c) || LOCAL_PART_SYMBOLS.Contains(c))) return false;
            // "a@gmail.com." reaches the same mailbox as "a@gmail.com"
            if (rawDomain.EndsWith('.')) return false;

            string ascii;
            try
            {
                ascii = Idn.GetAscii(rawDomain).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (ascii.Length > 253 || ascii.Split('.').Any(MixesScripts)) return false;

            domain = ascii;
            canonical = local + "@" + ascii;
            return true;
        }

        private static bool IsDotAtom(string value, Func<char, bool> isAllowed)
        {
            if (value.Length == 0 || value[0] == '.' || value[^1] == '.' || value.Contains("..")) return false;
            return value.All((c) => c == '.' || isAllowed(c));
        }

        // e.g. a Cyrillic "а" inside a Latin "gmail", the usual homograph trick
        private static bool MixesScripts(string asciiLabel)
        {
            if (!asciiLabel.StartsWith("xn--")) return false;
            string label;
            try
            {
                label = Idn.GetUnicode(asciiLabel);
            }
            catch (ArgumentException)
            {
                return true;
            }
            bool latin = false, greek = false, cyrillic = false;
            foreach (var c in label)
            {
                if (char.IsAsciiLetter(c) || (c >= 'À' && c <= 'ɏ')) latin = true;
                else if (c >= 'Ͱ' && c <= 'Ͽ') greek = true;
                else if (c >= 'Ѐ' && c <= 'ԯ') cyrillic = true;
            }
            return (latin ? 1 : 0) + (greek ? 1 : 0) + (cyrillic ? 1 : 0) > 1;
        }
    }

    // anything that isn't a valid address keeps Identity's default key, so existing accounts are found as before
    public class EmailLookupNormalizer : ILookupNormalizer
    {
        public string? NormalizeEmail(string? email) => Normalize(email);

        public string? NormalizeName(string? name) => Normalize(name);

        private static string? Normalize(string? value)
        {
            if (value == null) return null;
            return EmailCanonicalizer.TryCanonicalize(value, out var canonical, out _)
                ? canonical.ToUpperInvariant()
                : value.Normalize().ToUpperInvariant();
        }
    }
}
