using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Billing.Events;

namespace Kuaforum.Domain.Modules.Billing;

public enum CommissionStatementStatus
{
    Open = 0,
    Notified = 1,
    Paid = 2,
    Overdue = 3,
    SuspensionApplied = 4,
}

/// <summary>
/// RFC-012: aylık komisyon faturası. Matrah (BasisAmountMinor) yalnız dönemde Completed
/// olan randevuların TotalAmountMinor toplamı — walk-in dahil, iptal/no-show/pending hariç
/// (kapanış job'unun sorgu tarafı bu kuralı uygular, aggregate yalnız hesaplanmış toplamı alır).
/// RateApplied kapanış anında snapshot'lanır (per-shop override ?? global) — sonraki oran
/// değişiklikleri geçmiş statement'ı ETKİLEMEZ (RFC-006 DepositRate snapshot presedenti).
///
/// State machine (RFC-012):
///   Open → Notified → Paid | Overdue → SuspensionApplied → Paid
/// Paid'e geçiş hesabı OTOMATİK aktifleştirmez — admin ayrı bir BarberShop.Reactivate() çağırır.
/// </summary>
public sealed class CommissionStatement : AggregateRoot
{
    private CommissionStatement() { }

    public Guid BarberShopId { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public long BasisAmountMinor { get; private set; }
    public decimal RateApplied { get; private set; }
    public long AmountDueMinor { get; private set; }
    public DateOnly DueDate { get; private set; }
    public CommissionStatementStatus Status { get; private set; }
    public bool IsDisputed { get; private set; }
    public string? DisputeNote { get; private set; }
    public DateTimeOffset? DisputedAt { get; private set; }
    public string? AdminAdjustmentNote { get; private set; }

    public static Result<CommissionStatement> Create(
        Guid barberShopId,
        DateOnly periodStart,
        DateOnly periodEnd,
        long basisAmountMinor,
        decimal rateApplied)
    {
        if (periodEnd < periodStart)
            return Result.Failure<CommissionStatement>(Error.Validation("Dönem bitişi başlangıçtan önce olamaz."));
        if (basisAmountMinor <= 0)
            return Result.Failure<CommissionStatement>(Error.Validation("Matrah sıfırdan büyük olmalı."));
        if (rateApplied <= 0 || rateApplied > 0.5m)
            return Result.Failure<CommissionStatement>(Error.Validation("Komisyon oranı %0 ile %50 arasında olmalı."));

        return new CommissionStatement
        {
            BarberShopId = barberShopId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            BasisAmountMinor = basisAmountMinor,
            RateApplied = rateApplied,
            // Tek doğruluk kaynağı — Booking.Initiate dahil her komisyon hesabı buradan geçer.
            AmountDueMinor = PaymentAmounts.CalculateCommissionMinor(basisAmountMinor, rateApplied),
            DueDate = periodEnd.AddDays(7),
            Status = CommissionStatementStatus.Open,
        };
    }

    /// <summary>Kapanış job'u aynı tick'te Create() sonrası çağırır — kuaföre bildirim gider.</summary>
    public Result MarkNotified()
    {
        if (Status != CommissionStatementStatus.Open)
            return Result.Failure(Error.Validation($"{Status} durumundaki statement bildirime hazır değil."));

        Status = CommissionStatementStatus.Notified;
        Touch();

        RaiseDomainEvent(new CommissionStatementNotifiedEvent(Id, BarberShopId, PeriodStart, PeriodEnd, AmountDueMinor, DueDate));
        return Result.Success();
    }

    /// <summary>Günlük vade job'u — DueDate geçmiş Notified statement'ları Overdue'ya taşır.</summary>
    public Result MarkOverdue()
    {
        if (Status != CommissionStatementStatus.Notified)
            return Result.Failure(Error.Validation($"{Status} durumundaki statement gecikmiş olarak işaretlenemez."));

        Status = CommissionStatementStatus.Overdue;
        Touch();
        return Result.Success();
    }

    /// <summary>Aynı job tick'i — Overdue → SuspensionApplied. Çağıran taraf ayrıca BarberShop.Suspend() çağırmalı.</summary>
    public Result ApplySuspension()
    {
        if (Status != CommissionStatementStatus.Overdue)
            return Result.Failure(Error.Validation($"{Status} durumundaki statement için askı uygulanamaz."));

        Status = CommissionStatementStatus.SuspensionApplied;
        Touch();

        RaiseDomainEvent(new CommissionStatementSuspendedEvent(Id, BarberShopId, AmountDueMinor));
        return Result.Success();
    }

    /// <summary>
    /// Admin "ödendi işaretle" (havale onayı) veya CommissionPayment online tahsilat başarıyla
    /// tamamlandığında çağrılır. Hesabı OTOMATİK aktifleştirmez — RFC-012 madde 4: "iki ayrı adım".
    /// </summary>
    public Result MarkPaid()
    {
        if (Status is not (CommissionStatementStatus.Notified or CommissionStatementStatus.Overdue or CommissionStatementStatus.SuspensionApplied))
            return Result.Failure(Error.Validation($"{Status} durumundaki statement ödendi olarak işaretlenemez."));

        Status = CommissionStatementStatus.Paid;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Owner itirazı — yalnızca bir bayrak, ayrı bir status DEĞİL. Vade sayacını OTOMATİK
    /// durdurmaz (RFC-012 gerekçe: "itiraz et" ödemeden kaçınma aracına dönüşmesin).
    /// </summary>
    public Result Dispute(string note)
    {
        if (Status == CommissionStatementStatus.Paid)
            return Result.Failure(Error.Validation("Ödenmiş statement'a itiraz edilemez."));
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure(Error.Validation("İtiraz notu gerekli."));

        IsDisputed = true;
        DisputeNote = note.Length > 1000 ? note[..1000] : note;
        DisputedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    /// <summary>Admin tutar düzeltmesi — gerekçe zorunlu, audit'e de yazılır (caller sorumluluğunda).</summary>
    public Result AdjustAmount(long newAmountDueMinor, string reason)
    {
        if (Status == CommissionStatementStatus.Paid)
            return Result.Failure(Error.Validation("Ödenmiş statement tutarı düzeltilemez."));
        if (newAmountDueMinor < 0)
            return Result.Failure(Error.Validation("Tutar negatif olamaz."));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 8)
            return Result.Failure(Error.Validation("Düzeltme gerekçesi en az 8 karakter olmalı."));

        AmountDueMinor = newAmountDueMinor;
        AdminAdjustmentNote = reason.Length > 500 ? reason[..500] : reason;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Admin vade uzatma — RFC-012'de vade sayacını durdurabilen TEK mekanizma. Review fix:
    /// Overdue bir statement'ın vadesi gerçekten geleceğe (bugünden ileri) taşınırsa Notified'a
    /// GERİ ALINIR — aksi halde SuspendOverdueCommissionAccountsJob'un Overdue-retry taraması
    /// (bkz. job doc-comment) onu hâlâ askı adayı sanmaya devam eder. Yeni vade eski vadeden
    /// ileri ama HÂLÂ geçmişteyse (örn. admin 1 gün uzattı ama bu hâlâ dün) durum bilerek
    /// Overdue'da kalır — job bir sonraki koşumda zaten doğru şekilde yeniden değerlendirir.
    /// </summary>
    public Result ExtendDueDate(DateOnly newDueDate, string reason)
    {
        if (Status == CommissionStatementStatus.Paid)
            return Result.Failure(Error.Validation("Ödenmiş statement'ın vadesi uzatılamaz."));
        if (newDueDate <= DueDate)
            return Result.Failure(Error.Validation("Yeni vade mevcut vadeden ileri bir tarih olmalı."));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 8)
            return Result.Failure(Error.Validation("Vade uzatma gerekçesi en az 8 karakter olmalı."));

        DueDate = newDueDate;
        AdminAdjustmentNote = reason.Length > 500 ? reason[..500] : reason;

        if (Status == CommissionStatementStatus.Overdue && newDueDate > DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime))
            Status = CommissionStatementStatus.Notified;

        Touch();
        return Result.Success();
    }
}
