using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

/// <summary>
/// RFC-014 §2 D4 madde 1 / coordinator review (2026-08-07): randevu ÖZET adımında, oluşturmadan
/// ÖNCE çağrılan cross-shop çakışma ön-kontrolü. <see cref="InitiateBookingCommand"/>'ın
/// <c>ConflictingBooking</c> alanı ANCAK randevu zaten oluşturulduktan sonra dolar — kapora
/// akışında müşteri istemediği randevuyu görmek için önce ödeyip sonra iptal/iade olmak zorunda
/// kalırdı. Bu uç aynı bilgiyi randevu hiç oluşturulmadan verir; mobil "yine de devam etmek
/// istiyor musun?" diyaloğunu ÖNCEDEN gösterebilir.
///
/// Girdi tasarımı: <c>BarberShopId + ScheduledAt + ScheduledEndsAt</c> — StylistId/ServiceCatalogIds
/// DEĞİL. Mobil özet ekranı bu noktada zaten <c>ComputeAvailabilityQuery</c>'den dönen seçili
/// <c>SlotDto</c>'nun (StartsAt/EndsAt) ikisini de elinde tutuyor; süreyi hizmet listesinden
/// tekrar toplamak (InitiateBookingCommandHandler'daki <c>DurationMinutes+BufferMinutes</c>
/// hesabını burada YENİDEN YAZMAK) gereksiz bir ikinci "süre hesaplama" yolu açardı — iki yer
/// farklı sonuç üretirse (ör. hizmet süresi bu ikisi arasında değişirse) ön-kontrol ile gerçek
/// oluşturma farklı aralıkları karşılaştırır. Zaten bu uç yalnız TAVSİYE niteliğinde — asıl
/// yarış-güvenli kontrol <see cref="InitiateBookingCommand"/>'ın kendi ConflictingBooking alanı
/// (bkz. o alanın XML doc'u) — bu yüzden hafif kalması, çift hesaplama riski taşımaması tercih
/// edildi.
///
/// Yetkilendirme: <see cref="CustomerId"/> HER ZAMAN <c>IRequestContext.UserId</c>'den (JWT) gelir,
/// request body'den DEĞİL — endpoint bunu garanti eder (bkz. BookingEndpoints). Müşteri yalnız
/// KENDİ çakışmalarını sorgulayabilir; başka bir müşterinin randevu bilgisi (salon adı + saat
/// dahil, KVKK anlamında kişisel veri) hiçbir girdi kombinasyonuyla sızdırılamaz.
/// </summary>
public sealed record CheckBookingConflictQuery(
    Guid CustomerId,
    Guid BarberShopId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset ScheduledEndsAt) : IQuery<ConflictingBookingDto?>;

public sealed class CheckBookingConflictQueryValidator : AbstractValidator<CheckBookingConflictQuery>
{
    public CheckBookingConflictQueryValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.ScheduledEndsAt).GreaterThan(x => x.ScheduledAt)
            .WithMessage("Bitiş zamanı başlangıç zamanından sonra olmalı.");
    }
}

public sealed class CheckBookingConflictQueryHandler(IApplicationDbContext db)
    : IRequestHandler<CheckBookingConflictQuery, Result<ConflictingBookingDto?>>
{
    public async Task<Result<ConflictingBookingDto?>> Handle(CheckBookingConflictQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;

        // Paylaşılan sorgu — InitiateBookingCommandHandler ile AYNI mantık, bkz.
        // CrossShopConflictFinder XML doc. İki yol arasında davranış farkı doğmasın diye.
        var conflict = await CrossShopConflictFinder.FindAsync(
            ctx, request.CustomerId, request.BarberShopId, request.ScheduledAt, request.ScheduledEndsAt, ct);

        return Result.Success(conflict);
    }
}
