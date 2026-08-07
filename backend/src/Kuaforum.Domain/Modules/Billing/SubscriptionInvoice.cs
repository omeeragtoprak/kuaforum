using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Billing.Events;

namespace Kuaforum.Domain.Modules.Billing;

public enum SubscriptionInvoiceStatus
{
    Open = 0,
    Notified = 1,
    Paid = 2,
    Overdue = 3,
    SuspensionApplied = 4,
}

/// <summary>
/// RFC-014 §2 D1 (aylık sabit abonelik faturası — RFC-012'nin ciro-komisyonu faturası
/// <c>CommissionStatement</c>'ın yerini alır). Fatura → 7 gün vade → hatırlatma → askı →
/// itiraz → ödeme zinciri RFC-012'den AYNEN korunur (bkz. state machine aşağıda); yalnızca
/// tutarın kaynağı değişti: artık <c>ciro × oran</c> değil, doğrudan salonun bağlı olduğu
/// <see cref="SubscriptionPlan"/>.PriceMinor'ı. <see cref="PlanPriceMinor"/> kapanış anında
/// SNAPSHOT'lanır — sonraki bir plan fiyat değişikliği bu faturayı geriye dönük ETKİLEMEZ
/// (RFC-006 DepositRate / RFC-012 RateApplied snapshot presedentiyle aynı ilke).
///
/// PriceMinor == 0 veya null olan planlar (V1'de LAUNCH_FREE) için hiç fatura ÜRETİLMEZ —
/// bkz. CloseMonthlySubscriptionInvoicesJob.
///
/// State machine (RFC-012'den değişmeden):
///   Open → Notified → Paid | Overdue → SuspensionApplied → Paid
/// Paid'e geçiş hesabı OTOMATİK aktifleştirmez — admin ayrı bir BarberShop.Reactivate() çağırır.
/// </summary>
public sealed class SubscriptionInvoice : AggregateRoot
{
    private SubscriptionInvoice() { }

    public Guid BarberShopId { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public string PlanCode { get; private set; } = null!;

    /// <summary>Kapanış anında snapshot'lanan plan fiyatı — planın sonradan değişen fiyatından bağımsız, sabit referans.</summary>
    public long PlanPriceMinor { get; private set; }

    /// <summary>Fiilen tahsil edilecek/edilen tutar. Başlangıçta PlanPriceMinor'a eşittir; admin AdjustAmount ile değiştirebilir.</summary>
    public long AmountMinor { get; private set; }

    public DateOnly DueDate { get; private set; }
    public SubscriptionInvoiceStatus Status { get; private set; }
    public bool IsDisputed { get; private set; }
    public string? DisputeNote { get; private set; }
    public DateTimeOffset? DisputedAt { get; private set; }
    public string? AdminAdjustmentNote { get; private set; }

    public static Result<SubscriptionInvoice> Create(
        Guid barberShopId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string planCode,
        long planPriceMinor)
    {
        if (periodEnd < periodStart)
            return Result.Failure<SubscriptionInvoice>(Error.Validation("Dönem bitişi başlangıçtan önce olamaz."));
        if (string.IsNullOrWhiteSpace(planCode))
            return Result.Failure<SubscriptionInvoice>(Error.Validation("Plan kodu gerekli."));
        if (planPriceMinor <= 0)
            return Result.Failure<SubscriptionInvoice>(Error.Validation("Plan fiyatı sıfırdan büyük olmalı — ücretsiz planlar için fatura üretilmez."));

        return new SubscriptionInvoice
        {
            BarberShopId = barberShopId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PlanCode = planCode.Trim().ToUpperInvariant(),
            PlanPriceMinor = planPriceMinor,
            AmountMinor = planPriceMinor,
            DueDate = periodEnd.AddDays(7),
            Status = SubscriptionInvoiceStatus.Open,
        };
    }

    /// <summary>Kapanış job'u aynı tick'te Create() sonrası çağırır — kuaföre bildirim gider.</summary>
    public Result MarkNotified()
    {
        if (Status != SubscriptionInvoiceStatus.Open)
            return Result.Failure(Error.Validation($"{Status} durumundaki fatura bildirime hazır değil."));

        Status = SubscriptionInvoiceStatus.Notified;
        Touch();

        RaiseDomainEvent(new SubscriptionInvoiceNotifiedEvent(Id, BarberShopId, PeriodStart, PeriodEnd, AmountMinor, DueDate));
        return Result.Success();
    }

    /// <summary>Günlük vade job'u — DueDate geçmiş Notified fatura'ları Overdue'ya taşır.</summary>
    public Result MarkOverdue()
    {
        if (Status != SubscriptionInvoiceStatus.Notified)
            return Result.Failure(Error.Validation($"{Status} durumundaki fatura gecikmiş olarak işaretlenemez."));

        Status = SubscriptionInvoiceStatus.Overdue;
        Touch();
        return Result.Success();
    }

    /// <summary>Aynı job tick'i — Overdue → SuspensionApplied. Çağıran taraf ayrıca BarberShop.Suspend() çağırmalı.</summary>
    public Result ApplySuspension()
    {
        if (Status != SubscriptionInvoiceStatus.Overdue)
            return Result.Failure(Error.Validation($"{Status} durumundaki fatura için askı uygulanamaz."));

        Status = SubscriptionInvoiceStatus.SuspensionApplied;
        Touch();

        RaiseDomainEvent(new SubscriptionInvoiceSuspendedEvent(Id, BarberShopId, AmountMinor));
        return Result.Success();
    }

    /// <summary>
    /// Admin "ödendi işaretle" (havale onayı) veya SubscriptionInvoicePayment online tahsilat
    /// başarıyla tamamlandığında çağrılır. Hesabı OTOMATİK aktifleştirmez — RFC-012 madde 4:
    /// "iki ayrı adım".
    /// </summary>
    public Result MarkPaid()
    {
        if (Status is not (SubscriptionInvoiceStatus.Notified or SubscriptionInvoiceStatus.Overdue or SubscriptionInvoiceStatus.SuspensionApplied))
            return Result.Failure(Error.Validation($"{Status} durumundaki fatura ödendi olarak işaretlenemez."));

        Status = SubscriptionInvoiceStatus.Paid;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Owner itirazı — yalnızca bir bayrak, ayrı bir status DEĞİL. Vade sayacını OTOMATİK
    /// durdurmaz (RFC-012 gerekçe: "itiraz et" ödemeden kaçınma aracına dönüşmesin).
    /// </summary>
    public Result Dispute(string note)
    {
        if (Status == SubscriptionInvoiceStatus.Paid)
            return Result.Failure(Error.Validation("Ödenmiş fatura'ya itiraz edilemez."));
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure(Error.Validation("İtiraz notu gerekli."));

        IsDisputed = true;
        DisputeNote = note.Length > 1000 ? note[..1000] : note;
        DisputedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    /// <summary>Admin tutar düzeltmesi — gerekçe zorunlu, audit'e de yazılır (caller sorumluluğunda).</summary>
    public Result AdjustAmount(long newAmountMinor, string reason)
    {
        if (Status == SubscriptionInvoiceStatus.Paid)
            return Result.Failure(Error.Validation("Ödenmiş fatura tutarı düzeltilemez."));
        if (newAmountMinor < 0)
            return Result.Failure(Error.Validation("Tutar negatif olamaz."));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 8)
            return Result.Failure(Error.Validation("Düzeltme gerekçesi en az 8 karakter olmalı."));

        AmountMinor = newAmountMinor;
        AdminAdjustmentNote = reason.Length > 500 ? reason[..500] : reason;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Admin vade uzatma — RFC-012'de vade sayacını durdurabilen TEK mekanizma. Review fix:
    /// Overdue bir fatura'nın vadesi gerçekten geleceğe (bugünden ileri) taşınırsa Notified'a
    /// GERİ ALINIR — aksi halde SuspendOverdueSubscriptionsJob'un Overdue-retry taraması
    /// (bkz. job doc-comment) onu hâlâ askı adayı sanmaya devam eder. Yeni vade eski vadeden
    /// ileri ama HÂLÂ geçmişteyse (örn. admin 1 gün uzattı ama bu hâlâ dün) durum bilerek
    /// Overdue'da kalır — job bir sonraki koşumda zaten doğru şekilde yeniden değerlendirir.
    /// </summary>
    public Result ExtendDueDate(DateOnly newDueDate, string reason)
    {
        if (Status == SubscriptionInvoiceStatus.Paid)
            return Result.Failure(Error.Validation("Ödenmiş fatura'nın vadesi uzatılamaz."));
        if (newDueDate <= DueDate)
            return Result.Failure(Error.Validation("Yeni vade mevcut vadeden ileri bir tarih olmalı."));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 8)
            return Result.Failure(Error.Validation("Vade uzatma gerekçesi en az 8 karakter olmalı."));

        DueDate = newDueDate;
        AdminAdjustmentNote = reason.Length > 500 ? reason[..500] : reason;

        if (Status == SubscriptionInvoiceStatus.Overdue && newDueDate > DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime))
            Status = SubscriptionInvoiceStatus.Notified;

        Touch();
        return Result.Success();
    }
}
