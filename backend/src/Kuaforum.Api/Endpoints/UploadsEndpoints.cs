using Kuaforum.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Kuaforum.Api.Endpoints;

public static class UploadsEndpoints
{
    private const long MaxBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapUploadsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/uploads").WithTags("Uploads").RequireAuthorization();

        group.MapPost("/kyc", async (
            HttpRequest request,
            IObjectStorage storage,
            IRequestContext ctx,
            CancellationToken token) =>
        {
            if (ctx.UserId is null) return Results.Unauthorized();
            if (!request.HasFormContentType)
                return Results.Problem("multipart/form-data bekleniyor.", statusCode: 400);

            var form = await request.ReadFormAsync(token);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.Problem("Dosya gerekli.", statusCode: 400);
            if (file.Length > MaxBytes)
                return Results.Problem("Dosya 10 MB'tan büyük olamaz.", statusCode: 400);

            await using var stream = file.OpenReadStream();
            try
            {
                var key = await storage.SaveAsync(
                    stream,
                    file.ContentType,
                    file.Length,
                    folder: "kyc",
                    originalFileName: file.FileName,
                    ct: token);
                return Results.Ok(new UploadResponse(key, file.Length, file.ContentType));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        }).WithName("UploadKycDocument").DisableAntiforgery();

        return app;
    }

    public sealed record UploadResponse(string StorageKey, long SizeBytes, string ContentType);
}
