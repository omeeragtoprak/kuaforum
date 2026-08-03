using FluentValidation;
using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Consents;

/// <summary>
/// OTP-sonrası tek seferlik rıza ekranı (RFC-008) + mevcut kullanıcının ilk açılışında aynı
/// endpoint yeniden kullanılır. ToS kabulü zorunlu; Marketing opsiyonel. Güncel versiyonla zaten
/// granted ise no-op success (idempotent) — tekrar tıklama / çift request'te tekilleştirme.
/// </summary>
public sealed record GrantInitialConsentsCommand(
    Guid UserId,
    bool AcceptTos,
    bool AcceptMarketing) : ICommand;

public sealed class GrantInitialConsentsCommandValidator : AbstractValidator<GrantInitialConsentsCommand>
{
    public GrantInitialConsentsCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.AcceptTos)
            .Equal(true)
            .WithMessage("Kullanım Koşulları kabul edilmeden devam edilemez.");
    }
}

public sealed class GrantInitialConsentsCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<GrantInitialConsentsCommand, Result>
{
    public async Task<Result> Handle(GrantInitialConsentsCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var changed = false;

        var existingTos = await ctx.Set<Consent>()
            .Where(c => c.UserId == request.UserId && c.Purpose == ConsentPurpose.TermsOfService && c.RevokedAt == null && c.Granted)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingTos is null || existingTos.Version != LegalDocuments.CurrentTosVersion)
        {
            // Eski versiyonu supersede etme domain metodu üzerinden yapılır; UpdateConsentCommand'daki
            // "ToS revoke edilemez" iş kuralı yalnız EXPLICIT revoke isteğini (Grant=false) engeller,
            // burada olduğu gibi yeni versiyonla re-grant için önceki satırı kapatmayı değil.
            existingTos?.Revoke();

            var tosConsent = Consent.Grant(request.UserId, ConsentPurpose.TermsOfService, LegalDocuments.CurrentTosVersion, ConsentSource.Registration);
            ctx.Set<Consent>().Add(tosConsent);
            changed = true;
        }

        if (request.AcceptMarketing)
        {
            var existingMarketing = await ctx.Set<Consent>()
                .Where(c => c.UserId == request.UserId && c.Purpose == ConsentPurpose.Marketing && c.RevokedAt == null && c.Granted)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingMarketing is null || existingMarketing.Version != LegalDocuments.CurrentAydinlatmaVersion)
            {
                existingMarketing?.Revoke();

                var marketingConsent = Consent.Grant(request.UserId, ConsentPurpose.Marketing, LegalDocuments.CurrentAydinlatmaVersion, ConsentSource.Registration);
                ctx.Set<Consent>().Add(marketingConsent);
                changed = true;
            }
        }

        if (!changed)
            return Result.Success();

        await audit.RecordAsync(
            "consent.initial_granted",
            nameof(Consent),
            request.UserId.ToString(),
            actorUserId: request.UserId,
            payload: new { request.AcceptTos, request.AcceptMarketing, TosVersion = LegalDocuments.CurrentTosVersion },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
