using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Tests;

public class EmailAddressValidatorTests
{
    [Theory]
    [InlineData("user@gmail.com", "user@gmail.com")]
    [InlineData("first.last+notes@proton.me", "first.last+notes@proton.me")]
    [InlineData("o'brien@sub.example-mail.co.uk", "o'brien@sub.example-mail.co.uk")]
    [InlineData("user@GMAIL.COM", "user@gmail.com")]
    [InlineData("user@bücher.de", "user@xn--bcher-kva.de")]
    [InlineData("user＠ｇｍａｉｌ.com", "user@gmail.com")]
    public void CanonicalizesDeliverableAddresses(string email, string canonical)
    {
        Assert.True(EmailCanonicalizer.TryCanonicalize(email, out var actual, out _));
        Assert.Equal(canonical, actual);
    }

    // each was used on 2026-11-07 to register a second account for one mailbox, or to get past the disposable list
    [Theory]
    [InlineData("user@gmail.com.")]
    [InlineData("user@fldubai.com..")]
    [InlineData("user@fldubai.com..x")]
    [InlineData(".user@gmail.com")]
    [InlineData("us..er@gmail.com")]
    [InlineData("a@b@gmail.com")]
    [InlineData("josé@gmail.com")]
    [InlineData("user@gmаil.com")]
    [InlineData("\"user\"@gmail.com")]
    [InlineData("User <user@gmail.com>")]
    [InlineData("user@[127.0.0.1]")]
    [InlineData("user(comment)@gmail.com")]
    [InlineData("user@")]
    [InlineData("@gmail.com")]
    [InlineData(" user@gmail.com")]
    [InlineData("user@-gmail.com")]
    [InlineData("user@gma_il.com")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsMalformedAndLookAlikeAddresses(string? email)
    {
        Assert.False(EmailCanonicalizer.TryCanonicalize(email, out _, out _));
    }

    // existing accounts must still be found by the key Identity stored for them
    [Theory]
    [InlineData("User.Name+tag@Gmail.com")]
    [InlineData("someone@school.cdgfss..edu.hk")]
    [InlineData("typo@gmail.com.")]
    [InlineData("not an email")]
    public void KeepsIdentitysDefaultKeyForExistingForms(string value)
    {
        var normalizer = new EmailLookupNormalizer();
        var standard = new UpperInvariantLookupNormalizer();
        Assert.Equal(standard.NormalizeEmail(value), normalizer.NormalizeEmail(value));
        Assert.Equal(standard.NormalizeName(value), normalizer.NormalizeName(value));
    }

    [Fact]
    public void GivesLookAlikeFormsOfAnAddressOneKey()
    {
        var normalizer = new EmailLookupNormalizer();
        Assert.Equal(normalizer.NormalizeEmail("user@gmail.com"), normalizer.NormalizeEmail("user＠ｇｍａｉｌ.com"));
        Assert.Equal(normalizer.NormalizeEmail("user@xn--bcher-kva.de"), normalizer.NormalizeEmail("user@BÜCHER.de"));
    }

    [Fact]
    public async Task StoresTheCanonicalFormAndPassesItsSlotOn()
    {
        var check = new RecordingRiskCheck(new EmailRiskDecision(true, "slot-1"));
        var result = await new EmailAddressValidator(check).ValidateAsync("user@GMAIL.com", EmailCheckPurpose.Signup, "192.0.2.7");
        Assert.True(result.IsValid);
        Assert.Equal("user@gmail.com", result.Email);
        Assert.Equal("slot-1", result.Slot);
        Assert.Equal(("user@gmail.com", EmailCheckPurpose.Signup, "192.0.2.7"), check.Checked);
    }

    [Fact]
    public async Task NeverAsksAboutMalformedAddresses()
    {
        var check = new RecordingRiskCheck(new EmailRiskDecision(true));
        var result = await new EmailAddressValidator(check).ValidateAsync("user@gmail.com.", EmailCheckPurpose.Signup);
        Assert.False(result.IsValid);
        Assert.Null(check.Checked);
    }

    [Fact]
    public async Task RefusesWhatTheRiskCheckRefuses()
    {
        var result = await new EmailAddressValidator(new RecordingRiskCheck(new EmailRiskDecision(false, Message: "nope"))).ValidateAsync("user@gmail.com", EmailCheckPurpose.ChangeEmail);
        Assert.False(result.IsValid);
        Assert.Equal("nope", result.Error);
    }

    [Theory]
    [InlineData("""{"allow":true,"slot":"s1"}""", true, "s1", null)]
    [InlineData("""{"allow":false,"reason":"undeliverable"}""", false, null, "This email address can't receive mail.")]
    [InlineData("""{"allow":false,"reason":"domain_limit"}""", false, null, "Too many accounts were created with this email domain today. Please try again tomorrow or use a different email address.")]
    [InlineData("""{"allow":false,"reason":"something_new"}""", false, null, EmailAddressValidator.INVALID)]
    public async Task ReadsTheServicesDecision(string body, bool allow, string? slot, string? message)
    {
        var decision = await Http((_) => Respond(HttpStatusCode.OK, body)).CheckAsync("user@gmail.com", EmailCheckPurpose.Signup, null);
        Assert.Equal(new EmailRiskDecision(allow, slot, message), decision);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    public async Task AllowsTheAddressWhenTheServiceFails(HttpStatusCode status, string body)
    {
        var decision = await Http((_) => Respond(status, body)).CheckAsync("user@gmail.com", EmailCheckPurpose.Signup, null);
        Assert.Equal(new EmailRiskDecision(true), decision);
    }

    [Fact]
    public async Task AllowsTheAddressWhenTheServiceIsTooSlow()
    {
        var decision = await Http(async (ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Respond(HttpStatusCode.OK, """{"allow":false}"""); })
            .CheckAsync("user@gmail.com", EmailCheckPurpose.Signup, null);
        Assert.True(decision.Allow);
    }

    [Fact]
    public async Task SendsTheCheckAndConfirmation()
    {
        var requests = new List<(string Path, string Body)>();
        var check = Http(async (request) => { requests.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync())); return Respond(HttpStatusCode.OK, """{"allow":true,"slot":"s1"}"""); });
        await check.CheckAsync("user@gmail.com", EmailCheckPurpose.ChangeEmail, "192.0.2.7");
        await check.ConfirmAsync("s1", "u1");
        await check.ReleaseAsync("s2");
        Assert.Equal(("/v1/check", """{"email":"user@gmail.com","purpose":"change_email","client_ip":"192.0.2.7"}"""), requests[0]);
        Assert.Equal(("/v1/confirm", """{"slot":"s1","user_id":"u1"}"""), requests[1]);
        Assert.Equal(("/v1/release", """{"slot":"s2"}"""), requests[2]);
    }

    [Fact]
    public async Task NeverFailsBecauseOfTheService()
    {
        Func<HttpRequestMessage, HttpResponseMessage> fail = (_) => throw new InvalidOperationException("anything");
        var check = Http(fail);
        Assert.True((await check.CheckAsync("user@gmail.com", EmailCheckPurpose.Signup, null)).Allow);
        await check.ConfirmAsync("s1", "u1");
        await check.ReleaseAsync("s1");
    }

    private static HttpEmailRiskCheck Http(Func<CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(new HttpClient(new Handler((_, ct) => respond(ct))) { BaseAddress = new Uri("http://email-validator.test/") }, NullLogger<HttpEmailRiskCheck>.Instance);

    private static HttpEmailRiskCheck Http(Func<HttpRequestMessage, HttpResponseMessage> respond) => Http((HttpRequestMessage r) => Task.FromResult(respond(r)));

    private static HttpEmailRiskCheck Http(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new(new HttpClient(new Handler((request, _) => respond(request))) { BaseAddress = new Uri("http://email-validator.test/") }, NullLogger<HttpEmailRiskCheck>.Instance);

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private sealed class RecordingRiskCheck(EmailRiskDecision decision) : IEmailRiskCheck
    {
        public (string, EmailCheckPurpose, string?)? Checked { get; private set; }

        public Task<EmailRiskDecision> CheckAsync(string email, EmailCheckPurpose purpose, string? clientIp)
        {
            Checked = (email, purpose, clientIp);
            return Task.FromResult(decision);
        }

        public Task ConfirmAsync(string slot, string userId) => Task.CompletedTask;

        public Task ReleaseAsync(string slot) => Task.CompletedTask;
    }
}
