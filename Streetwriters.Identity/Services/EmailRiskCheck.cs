using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Streetwriters.Identity.Services
{
    public enum EmailCheckPurpose
    {
        Signup,
        ChangeEmail
    }

    /// <param name="Slot">Set when the check counted the signup; confirm it once the account exists.</param>
    public record EmailRiskDecision(bool Allow, string? Slot = null, string? Message = null);

    /// <summary>A deployment's own checks of an address beyond its syntax; self-hosted servers get <see cref="NoEmailRiskCheck"/>.</summary>
    public interface IEmailRiskCheck
    {
        Task<EmailRiskDecision> CheckAsync(string email, EmailCheckPurpose purpose, string? clientIp);

        Task ConfirmAsync(string slot, string userId);

        Task ReleaseAsync(string slot);
    }

    public class NoEmailRiskCheck : IEmailRiskCheck
    {
        public Task<EmailRiskDecision> CheckAsync(string email, EmailCheckPurpose purpose, string? clientIp) => Task.FromResult(new EmailRiskDecision(true));

        public Task ConfirmAsync(string slot, string userId) => Task.CompletedTask;

        public Task ReleaseAsync(string slot) => Task.CompletedTask;
    }

    public class HttpEmailRiskCheck(HttpClient httpClient, ILogger<HttpEmailRiskCheck> logger) : IEmailRiskCheck
    {
        private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(2);

        public async Task<EmailRiskDecision> CheckAsync(string email, EmailCheckPurpose purpose, string? clientIp)
        {
            try
            {
                using var cts = new CancellationTokenSource(TIMEOUT);
                using var response = await httpClient.PostAsJsonAsync("v1/check", new CheckRequest(email, purpose == EmailCheckPurpose.Signup ? "signup" : "change_email", clientIp), cts.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<CheckResponse>(cts.Token) ?? throw new JsonException("Empty response");
                if (result.Allow) return new EmailRiskDecision(true, result.Slot);
                return new EmailRiskDecision(false, Message: result.Reason switch
                {
                    "undeliverable" => "This email address can't receive mail.",
                    "domain_limit" => "Too many accounts were created with this email domain today. Please try again tomorrow or use a different email address.",
                    _ => EmailAddressValidator.INVALID
                });
            }
            // any failure, including timeouts mid-body and non-JSON answers: this check must never be what fails a signup
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The email validator couldn't be reached; allowing the address");
                return new EmailRiskDecision(true);
            }
        }

        public Task ConfirmAsync(string slot, string userId) => PostAsync("v1/confirm", new ConfirmRequest(slot, userId), slot);

        public Task ReleaseAsync(string slot) => PostAsync("v1/release", new ReleaseRequest(slot), slot);

        // a lost confirmation only makes the slot expire sooner, and a lost release only keeps it counted for its hour
        private async Task PostAsync<T>(string path, T body, string slot)
        {
            try
            {
                using var cts = new CancellationTokenSource(TIMEOUT);
                using var response = await httpClient.PostAsJsonAsync(path, body, cts.Token);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send {Path} for email validator slot {Slot}", path, slot);
            }
        }

        private sealed record CheckRequest(
            [property: JsonPropertyName("email")] string Email,
            [property: JsonPropertyName("purpose")] string Purpose,
            [property: JsonPropertyName("client_ip")] string? ClientIp);

        private sealed record ConfirmRequest(
            [property: JsonPropertyName("slot")] string Slot,
            [property: JsonPropertyName("user_id")] string UserId);

        private sealed record ReleaseRequest([property: JsonPropertyName("slot")] string Slot);

        private sealed record CheckResponse(
            [property: JsonPropertyName("allow")] bool Allow,
            [property: JsonPropertyName("slot")] string? Slot,
            [property: JsonPropertyName("reason")] string? Reason);
    }
}
