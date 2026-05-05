using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Identity.Consents;

public sealed record UpdateConsentCommand(
    Guid UserId,
    ConsentPurpose Purpose,
    bool Grant,
    string Version) : ICommand;

public sealed class UpdateConsentCommandValidator : AbstractValidator<UpdateConsentCommand>
{
    public UpdateConsentCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Purpose).IsInEnum();
        RuleFor(x => x.Version).NotEmpty().MaximumLength(20);
    }
}

public sealed class UpdateConsentCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<UpdateConsentCommand, Result>
{
    public async Task<Result> Handle(UpdateConsentCommand request, CancellationToken cancellationToken)
    {
        if (request.Purpose == ConsentPurpose.TermsOfService && !request.Grant)
            return Result.Failure(Error.Validation("Hesap aktifken Hizmet Şartları rızası geri alınamaz. Onun yerine hesap silme akışını kullanın."));

        var ctx = (DbContext)db;
        var existing = await ctx.Set<Consent>()
            .Where(c => c.UserId == request.UserId && c.Purpose == request.Purpose && c.RevokedAt == null && c.Granted)
            .FirstOrDefaultAsync(cancellationToken);

        if (request.Grant)
        {
            if (existing is not null && existing.Version == request.Version)
                return Result.Success();

            existing?.Revoke();

            var consent = Consent.Grant(request.UserId, request.Purpose, request.Version, ConsentSource.SettingsUpdate);
            ctx.Set<Consent>().Add(consent);

            await audit.RecordAsync(
                "user.consent.granted",
                nameof(Consent),
                consent.Id.ToString(),
                actorUserId: request.UserId,
                payload: new { request.Purpose, request.Version },
                ct: cancellationToken);
        }
        else
        {
            if (existing is null) return Result.Success();

            var revoke = existing.Revoke();
            if (revoke.IsFailure) return revoke;

            await audit.RecordAsync(
                "user.consent.revoked",
                nameof(Consent),
                existing.Id.ToString(),
                actorUserId: request.UserId,
                payload: new { request.Purpose },
                ct: cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
