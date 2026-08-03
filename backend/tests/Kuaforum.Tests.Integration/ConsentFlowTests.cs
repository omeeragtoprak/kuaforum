using System.Net;
using System.Net.Http.Json;
using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

// RFC-008 K1: legal metin servisi + initial-consent + requiresTosConsent.
[Collection(nameof(IntegrationTestCollection))]
public sealed class ConsentFlowTests(IntegrationTestFactory factory)
{
    public sealed record LegalDocResp(string Doc, string Version, string Title, string Markdown);

    public sealed record MyProfileResp(
        Guid Id, string? Phone, string? Email, string? DisplayName, string? AvatarUrl,
        string Role, string Status, string Locale, string TimeZone,
        DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, DateTimeOffset? DeletedAt,
        bool RequiresTosConsent);

    [Fact]
    public async Task Legal_endpoint_returns_document_with_version_and_title()
    {
        using var client = factory.CreateClient();

        var aydinlatma = await client.GetJson<LegalDocResp>("/v1/legal/aydinlatma");
        Assert.Equal("aydinlatma", aydinlatma.Doc);
        Assert.Equal("v1", aydinlatma.Version);
        Assert.False(string.IsNullOrWhiteSpace(aydinlatma.Title));
        Assert.Contains("PLACEHOLDER", aydinlatma.Markdown);

        var tos = await client.GetJson<LegalDocResp>("/v1/legal/kullanim-kosullari");
        Assert.Equal("kullanim-kosullari", tos.Doc);
        Assert.Equal("v1", tos.Version);
        Assert.False(string.IsNullOrWhiteSpace(tos.Title));
        Assert.Contains("PLACEHOLDER", tos.Markdown);
    }

    [Fact]
    public async Task Legal_endpoint_returns_404_for_unknown_doc()
    {
        using var client = factory.CreateClient();

        var resp = await client.GetAsync("/v1/legal/unknown-doc");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Fresh_user_requires_tos_consent_until_initial_grant()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        // taze kullanıcı: hiç ToS rızası yok → true
        var before = await client.GetJson<MyProfileResp>("/v1/users/me");
        Assert.True(before.RequiresTosConsent);

        var grant = await client.PostJsonAsync("/v1/users/me/consents/initial", new { acceptTos = true, acceptMarketing = true });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);

        var after = await client.GetJson<MyProfileResp>("/v1/users/me");
        Assert.False(after.RequiresTosConsent);
    }

    [Fact]
    public async Task Initial_grant_persists_tos_and_marketing_consents_and_writes_audit_log()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var grant = await client.PostJsonAsync("/v1/users/me/consents/initial", new { acceptTos = true, acceptMarketing = true });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();

        var tos = await db.Set<Consent>()
            .SingleAsync(c => c.UserId == login.UserId && c.Purpose == ConsentPurpose.TermsOfService);
        Assert.True(tos.Granted);
        Assert.Equal("v1", tos.Version);
        Assert.Null(tos.RevokedAt);

        var marketing = await db.Set<Consent>()
            .SingleAsync(c => c.UserId == login.UserId && c.Purpose == ConsentPurpose.Marketing);
        Assert.True(marketing.Granted);
        Assert.Null(marketing.RevokedAt);

        var audit = await db.Set<AuditLog>()
            .Where(a => a.ActionType == "consent.initial_granted" && a.ActorUserId == login.UserId)
            .ToListAsync();
        Assert.Single(audit);
        Assert.Equal(login.UserId.ToString(), audit[0].ResourceId);
    }

    [Fact]
    public async Task Initial_grant_without_accepting_tos_returns_400()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var resp = await client.PostJsonAsync("/v1/users/me/consents/initial", new { acceptTos = false, acceptMarketing = false });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
        var hasTos = await db.Set<Consent>().AnyAsync(c => c.UserId == login.UserId && c.Purpose == ConsentPurpose.TermsOfService);
        Assert.False(hasTos, "acceptTos=false hiçbir ToS rızası yaratmamalı");
    }

    [Fact]
    public async Task Initial_grant_is_idempotent_on_repeated_call()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        var first = await client.PostJsonAsync("/v1/users/me/consents/initial", new { acceptTos = true, acceptMarketing = true });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.PostJsonAsync("/v1/users/me/consents/initial", new { acceptTos = true, acceptMarketing = true });
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();

        // ikinci çağrı yeni satır yaratmamalı — tek granted ToS + tek granted Marketing satırı kalmalı.
        var tosRows = await db.Set<Consent>()
            .Where(c => c.UserId == login.UserId && c.Purpose == ConsentPurpose.TermsOfService)
            .ToListAsync();
        Assert.Single(tosRows);
        Assert.True(tosRows[0].Granted);

        var marketingRows = await db.Set<Consent>()
            .Where(c => c.UserId == login.UserId && c.Purpose == ConsentPurpose.Marketing)
            .ToListAsync();
        Assert.Single(marketingRows);
        Assert.True(marketingRows[0].Granted);

        // idempotent tekrarda audit tekrar yazılmamalı (gerçek bir state değişikliği yok).
        var auditCount = await db.Set<AuditLog>()
            .CountAsync(a => a.ActionType == "consent.initial_granted" && a.ActorUserId == login.UserId);
        Assert.Equal(1, auditCount);
    }

    [Fact]
    public async Task Stale_version_tos_consent_still_requires_re_consent()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        // eski versiyon (v0) rızası — hukuki metin güncellendiğinde otomatik yeniden sorulmalı.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var staleConsent = Consent.Grant(login.UserId, ConsentPurpose.TermsOfService, "v0", ConsentSource.Registration);
            db.Set<Consent>().Add(staleConsent);
            await db.SaveChangesAsync();
        }

        var profile = await client.GetJson<MyProfileResp>("/v1/users/me");
        Assert.True(profile.RequiresTosConsent, "Eski versiyon ToS rızası güncel sayılmamalı");
    }

    [Fact]
    public async Task Initial_grant_supersedes_stale_tos_consent_row()
    {
        await factory.ResetDatabaseAsync();

        using var client = factory.CreateClient();
        var login = await client.DevLoginAsync("Customer");
        client.SetBearer(login.AccessToken);

        // en riskli dal: kullanıcının ELİNDE zaten GRANTED ama eski versiyon (v0) ToS satırı var
        // (ör. hukuk metni v1'e güncellendi). Initial-grant çağrısı bu satırı revoke edip yerine
        // güncel v1 satırı yazmalı — hem eski satırın hem yeni satırın state'i doğru olmalı.
        Guid staleConsentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
            var staleConsent = Consent.Grant(login.UserId, ConsentPurpose.TermsOfService, "v0", ConsentSource.Registration);
            db.Set<Consent>().Add(staleConsent);
            await db.SaveChangesAsync();
            staleConsentId = staleConsent.Id;
        }

        var grant = await client.PostJsonAsync("/v1/users/me/consents/initial", new { acceptTos = true, acceptMarketing = false });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();

            var staleRow = await db.Set<Consent>().SingleAsync(c => c.Id == staleConsentId);
            Assert.False(staleRow.Granted, "Eski v0 satırı supersede sonrası Granted=false olmalı");
            Assert.NotNull(staleRow.RevokedAt);
            Assert.Equal("v0", staleRow.Version);

            var activeRow = await db.Set<Consent>()
                .SingleAsync(c => c.UserId == login.UserId
                    && c.Purpose == ConsentPurpose.TermsOfService
                    && c.Id != staleConsentId);
            Assert.True(activeRow.Granted);
            Assert.Null(activeRow.RevokedAt);
            Assert.Equal("v1", activeRow.Version);
        }

        var profile = await client.GetJson<MyProfileResp>("/v1/users/me");
        Assert.False(profile.RequiresTosConsent, "Supersede sonrası güncel v1 rızası granted olmalı");
    }
}
