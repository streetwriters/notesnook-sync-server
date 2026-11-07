using System.Threading.Tasks;

namespace Streetwriters.Identity.Services
{
    public record EmailValidationResult(bool IsValid, string? Email, string? Error, string? Slot = null);

    public class EmailAddressValidator(IEmailRiskCheck riskCheck)
    {
        public const string INVALID = "Invalid email address.";

        public async Task<EmailValidationResult> ValidateAsync(string? email, EmailCheckPurpose purpose, string? clientIp = null)
        {
            if (!EmailCanonicalizer.TryCanonicalize(email, out var canonical, out _)) return new(false, null, INVALID);
            var decision = await riskCheck.CheckAsync(canonical, purpose, clientIp);
            return decision.Allow ? new(true, canonical, null, decision.Slot) : new(false, null, decision.Message ?? INVALID);
        }

        public Task ConfirmAsync(string? slot, string userId) => slot == null ? Task.CompletedTask : riskCheck.ConfirmAsync(slot, userId);

        public Task ReleaseAsync(string? slot) => slot == null ? Task.CompletedTask : riskCheck.ReleaseAsync(slot);
    }
}
