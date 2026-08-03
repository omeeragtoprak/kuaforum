using Kuaforum.Application.Common;

namespace Kuaforum.Api.Endpoints;

public static class LegalEndpoints
{
    // Slug → (başlık, güncel versiyon, embedded resource logical name).
    // Versiyon LegalDocuments sabitlerinden okunur — rıza kaydıyla (Consent.Version) aynı
    // kaynak olması RFC-008'in tek doğruluk kaynağı ilkesi.
    private static readonly IReadOnlyDictionary<string, LegalDocumentMeta> Documents =
        new Dictionary<string, LegalDocumentMeta>(StringComparer.Ordinal)
        {
            [LegalDocuments.AydinlatmaSlug] = new(
                "Kişisel Verilerin İşlenmesine İlişkin Aydınlatma Metni",
                LegalDocuments.CurrentAydinlatmaVersion,
                "Kuaforum.Api.Legal.aydinlatma-v1.md"),
            [LegalDocuments.KullanimKosullariSlug] = new(
                "Kullanım Koşulları",
                LegalDocuments.CurrentTosVersion,
                "Kuaforum.Api.Legal.kullanim-kosullari-v1.md"),
        };

    public static IEndpointRouteBuilder MapLegalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/legal").WithTags("Legal").AllowAnonymous();

        group.MapGet("/{doc}", GetLegalDocument).WithName("GetLegalDocument");

        return app;
    }

    private static async Task<IResult> GetLegalDocument(string doc, CancellationToken ct)
    {
        if (!Documents.TryGetValue(doc, out var meta))
            return Results.NotFound();

        var assembly = typeof(LegalEndpoints).Assembly;
        await using var stream = assembly.GetManifestResourceStream(meta.ResourceName);
        if (stream is null)
            return Results.NotFound();

        using var reader = new StreamReader(stream);
        var markdown = await reader.ReadToEndAsync(ct);

        return Results.Ok(new LegalDocumentResponse(doc, meta.Version, meta.Title, markdown));
    }

    private sealed record LegalDocumentMeta(string Title, string Version, string ResourceName);

    public sealed record LegalDocumentResponse(string Doc, string Version, string Title, string Markdown);
}
