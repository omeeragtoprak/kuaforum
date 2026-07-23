using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Integration;

[Collection(nameof(IntegrationTestCollection))]
public sealed class KycFlowTests(IntegrationTestFactory factory)
{
    private sealed record DraftShopDto(Guid Id, Guid OwnerId, string Name, string Status);
    private sealed record SubmitKycResp(Guid SubmissionId);
    private sealed record UploadResponse(string StorageKey, long SizeBytes, string ContentType);
    private sealed record AdminKycDocDto(Guid Id, string DocumentType, string FileName, string? ContentType, long SizeBytes, DateTimeOffset UploadedAt);
    private sealed record NotificationItemDto(Guid Id, string TemplateKey, string Subject, string Body, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt, Guid? RelatedBookingId);
    private sealed record NotificationFeedDto(int UnreadCount, IReadOnlyList<NotificationItemDto> Items);

    private static async Task SeedAdminAsync(IntegrationTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.Seeding.DemoBarberSeeder>();
        await seeder.SeedAsync(default);

        var db = scope.ServiceProvider.GetRequiredService<Kuaforum.Infrastructure.Persistence.ApplicationDbContext>();
        var emailRes = Kuaforum.Domain.Modules.Identity.ValueObjects.EmailAddress.TryCreate("admin-kyc@kuaforum.test");
        var adminRes = Kuaforum.Domain.Modules.Identity.User.CreateAdmin(emailRes.Value, "KYC Test Admin");
        db.Users.Add(adminRes.Value);
        await db.SaveChangesAsync(default);
    }

    private static async Task<string> UploadFakePdfAsync(HttpClient client, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 fake test bytes");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", fileName);

        var resp = await client.PostAsync("/v1/uploads/kyc", content);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<UploadResponse>(TestHelpers.Json);
        return body!.StorageKey;
    }

    [Fact]
    public async Task Kyc_reject_reverts_shop_to_draft_and_resubmit_approve_activates_with_notifications()
    {
        await factory.ResetDatabaseAsync();
        await SeedAdminAsync(factory);

        using var draftClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        draftClient.SetBearer((await draftClient.DevLoginAsync("BarberOwnerDraft")).AccessToken);
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);

        var shop = await draftClient.GetJson<DraftShopDto>("/v1/owner/barber-shop");
        Assert.Equal("Draft", shop.Status);

        // --- first submission → rejected ---
        var taxKey1 = await UploadFakePdfAsync(draftClient, "vergi-1.pdf");
        var permitKey1 = await UploadFakePdfAsync(draftClient, "ruhsat-1.pdf");

        var submit1 = await draftClient.PostAsJsonAsync($"/v1/owner/barber-shop/{shop.Id}/kyc/submit", new
        {
            documents = new[]
            {
                new { documentType = "TaxLicense", fileName = "vergi-1.pdf", storageKey = taxKey1, sizeBytes = 64L, contentType = "application/pdf" },
                new { documentType = "SanitaryPermit", fileName = "ruhsat-1.pdf", storageKey = permitKey1, sizeBytes = 64L, contentType = "application/pdf" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, submit1.StatusCode);
        var submission1 = (await submit1.Content.ReadFromJsonAsync<SubmitKycResp>(TestHelpers.Json))!;

        var afterSubmit1 = await draftClient.GetJson<DraftShopDto>("/v1/owner/barber-shop");
        Assert.Equal("PendingKyc", afterSubmit1.Status);

        var rejectionReason = "Vergi levhası eksik — sayfa 2 okunamıyor.";
        var reject = await adminClient.PostAsJsonAsync($"/v1/admin/kyc/{submission1.SubmissionId}/review", new
        {
            approve = false,
            rejectionReason,
            subMerchantKey = (string?)null,
        });
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);

        // GAP fix under test: shop must revert to Draft, not stay stuck in PendingKyc forever.
        var afterReject = await draftClient.GetJson<DraftShopDto>("/v1/owner/barber-shop");
        Assert.Equal("Draft", afterReject.Status);

        // owner sees the rejection with reason in their in-app notification feed
        var feedAfterReject = await draftClient.GetJson<NotificationFeedDto>("/v1/notifications");
        var rejectNotification = feedAfterReject.Items.SingleOrDefault(i => i.TemplateKey == "kyc.rejected");
        Assert.NotNull(rejectNotification);
        Assert.Contains(rejectionReason, rejectNotification!.Body);

        // --- resubmission must now succeed (previously blocked forever) ---
        var taxKey2 = await UploadFakePdfAsync(draftClient, "vergi-2.pdf");
        var permitKey2 = await UploadFakePdfAsync(draftClient, "ruhsat-2.pdf");

        var submit2 = await draftClient.PostAsJsonAsync($"/v1/owner/barber-shop/{shop.Id}/kyc/submit", new
        {
            documents = new[]
            {
                new { documentType = "TaxLicense", fileName = "vergi-2.pdf", storageKey = taxKey2, sizeBytes = 64L, contentType = "application/pdf" },
                new { documentType = "SanitaryPermit", fileName = "ruhsat-2.pdf", storageKey = permitKey2, sizeBytes = 64L, contentType = "application/pdf" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, submit2.StatusCode);
        var submission2 = (await submit2.Content.ReadFromJsonAsync<SubmitKycResp>(TestHelpers.Json))!;

        var afterSubmit2 = await draftClient.GetJson<DraftShopDto>("/v1/owner/barber-shop");
        Assert.Equal("PendingKyc", afterSubmit2.Status);

        var approve = await adminClient.PostAsJsonAsync($"/v1/admin/kyc/{submission2.SubmissionId}/review", new
        {
            approve = true,
            rejectionReason = (string?)null,
            subMerchantKey = "PAYTR-TEST-KYC-FLOW",
        });
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);

        var afterApprove = await draftClient.GetJson<DraftShopDto>("/v1/owner/barber-shop");
        Assert.Equal("Active", afterApprove.Status);

        // owner also gets an approval notification
        var feedAfterApprove = await draftClient.GetJson<NotificationFeedDto>("/v1/notifications");
        Assert.Contains(feedAfterApprove.Items, i => i.TemplateKey == "kyc.approved");
    }

    [Fact]
    public async Task Admin_can_view_kyc_documents_customer_forbidden_and_missing_ids_404()
    {
        await factory.ResetDatabaseAsync();
        await SeedAdminAsync(factory);

        using var draftClient = factory.CreateClient();
        using var adminClient = factory.CreateClient();
        using var customerClient = factory.CreateClient();
        draftClient.SetBearer((await draftClient.DevLoginAsync("BarberOwnerDraft")).AccessToken);
        adminClient.SetBearer((await adminClient.DevLoginAsync("Admin")).AccessToken);
        customerClient.SetBearer((await customerClient.DevLoginAsync("Customer")).AccessToken);

        var shop = await draftClient.GetJson<DraftShopDto>("/v1/owner/barber-shop");

        var taxBytes = Encoding.UTF8.GetBytes("%PDF-1.4 vergi levhasi test bytes");
        var taxKey = await UploadBytesAsync(draftClient, "vergi.pdf", "application/pdf", taxBytes);
        var permitKey = await UploadFakePdfAsync(draftClient, "ruhsat.pdf");

        var submit = await draftClient.PostAsJsonAsync($"/v1/owner/barber-shop/{shop.Id}/kyc/submit", new
        {
            documents = new[]
            {
                new { documentType = "TaxLicense", fileName = "vergi.pdf", storageKey = taxKey, sizeBytes = (long)taxBytes.Length, contentType = "application/pdf" },
                new { documentType = "SanitaryPermit", fileName = "ruhsat.pdf", storageKey = permitKey, sizeBytes = 64L, contentType = "application/pdf" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var submission = (await submit.Content.ReadFromJsonAsync<SubmitKycResp>(TestHelpers.Json))!;

        // admin: list documents
        var docs = await adminClient.GetJson<IReadOnlyList<AdminKycDocDto>>($"/v1/admin/kyc/{submission.SubmissionId}/documents");
        Assert.Equal(2, docs.Count);
        var taxDoc = docs.Single(d => d.FileName == "vergi.pdf");
        Assert.Equal("TaxLicense", taxDoc.DocumentType);
        Assert.Equal("application/pdf", taxDoc.ContentType);
        Assert.Equal(taxBytes.Length, taxDoc.SizeBytes);

        // admin: fetch content, bytes + content-type round-trip
        var contentResp = await adminClient.GetAsync($"/v1/admin/kyc/documents/{taxDoc.Id}/content");
        Assert.Equal(HttpStatusCode.OK, contentResp.StatusCode);
        Assert.Equal("application/pdf", contentResp.Content.Headers.ContentType?.MediaType);
        var returnedBytes = await contentResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(taxBytes, returnedBytes);

        // customer: forbidden from both endpoints
        var customerList = await customerClient.GetAsync($"/v1/admin/kyc/{submission.SubmissionId}/documents");
        Assert.True(
            customerList.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Customer belge listesine erişememeli (got {(int)customerList.StatusCode})");

        var customerContent = await customerClient.GetAsync($"/v1/admin/kyc/documents/{taxDoc.Id}/content");
        Assert.True(
            customerContent.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Customer belge içeriğine erişememeli (got {(int)customerContent.StatusCode})");

        // admin: unknown ids → 404
        var missingSubmission = await adminClient.GetAsync($"/v1/admin/kyc/{Guid.NewGuid()}/documents");
        Assert.Equal(HttpStatusCode.NotFound, missingSubmission.StatusCode);

        var missingDocument = await adminClient.GetAsync($"/v1/admin/kyc/documents/{Guid.NewGuid()}/content");
        Assert.Equal(HttpStatusCode.NotFound, missingDocument.StatusCode);
    }

    private static async Task<string> UploadBytesAsync(HttpClient client, string fileName, string contentType, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        var resp = await client.PostAsync("/v1/uploads/kyc", content);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<UploadResponse>(TestHelpers.Json);
        return body!.StorageKey;
    }
}
