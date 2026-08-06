# PROGRESS.md — Tek Gerçek Kaynak: Güncel Durum Özeti

**Tarih:** 2026-08-06
**Kaynak:** `git log --oneline` (RFC-001..013 zinciri, `f011838` HEAD), `docs/GAP-ANALYSIS.md` (2026-07-23 tam tarama + nokta güncellemeleri), ve bu FAZ 1 turunda (`docs/DOMAIN.md`, `docs/GAPS.md`) kodda doğrudan doğrulanan bulgular.
**Amaç:** "Şu an proje nerede?" sorusuna tek yerden, güncel, kanıtlı cevap. Detay için `docs/GAP-ANALYSIS.md` (boşluklar) ve `docs/rfc/` (kararlar) referans alınır — burada kopyalanmaz.

---

## 1. Yönetici Özeti

Proje **greenfield değil** — Cycle 12/13 seviyesinde, 13 kabul edilmiş RFC (RFC-001..013), üç UI yüzeyi (mobile Expo, web-admin Next.js, yeni web landing) gerçek backend endpoint'lerine bağlı. Booking/Payment/Availability domain çekirdeği FAZ 1 denetiminde **kod üzerinden yeniden doğrulandı**: randevu state machine, iptal→otomatik-iade zinciri, çift-rezervasyon DB garantisi (M15 EXCLUDE constraint) ve Pending-timeout süpürme mekanizması **çalışır durumda ve test kanıtlı**. `docs/GAP-ANALYSIS.md`'nin 2026-07-23 tarihli P0/P1 envanterinin bir kısmı (P0-4, P1-1, P1-3) bu turda kod üzerinden **kapalı** doğrulandı ama GAP-ANALYSIS.md metninde bu kapanış henüz not düşülmemiş — bkz. §4.

Üç yeni iş modeli kararı (RFC-009 ücretsiz büyüme modu, RFC-012 aylık ciro komisyonu, RFC-013 per-shop kapora) 2026-08-05'te alınıp aynı gün/ertesi gün implement edildi — `CommissionStatement`/`CommissionPayment` yeni bir "Billing" modülü olarak koda girdi (orijinal M1-M16 modül listesinde yok, bkz. §3).

Deploy edilebilirlik, gerçek PSP/SMS/e-Arşiv entegrasyonları ve bazı yasal doğrulamalar hâlâ founder-bloklu (🟣) — bu FAZ 1 turunun kapsamı dışında, `docs/GAP-ANALYSIS.md` §10'da listeli.

---

## 2. Test Envanteri

| Katman | Dosya/attribute sayısı (grep, 2026-08-06) | Not |
|---|---|---|
| Backend Domain (`Kuaforum.Tests.Domain`) | 170 `[Fact]`/`[Theory]` | `backend/tests/Kuaforum.Tests.Domain` |
| Backend Application (`Kuaforum.Tests.Application`) | 62 `[Fact]`/`[Theory]` | `backend/tests/Kuaforum.Tests.Application` |
| Backend Integration (`Kuaforum.Tests.Integration`) | 176 `[Fact]`/`[Theory]` | `backend/tests/Kuaforum.Tests.Integration`, Testcontainers (Postgres+Redis) |
| **Backend toplam (attribute sayımı)** | **408** | `[unverified 2026-08-06 — dotnet test bu turda çalıştırılmadı (repo kökünde .sln yok, çözüm dosyası bulunamadı); sayı grep ile [Fact]/[Theory] işaretleyicisi sayılarak çıkarıldı, Theory+InlineData çalışma-zamanı test case sayısını artırabilir]` |
| Mobile Jest | 11 test dosyası | `mobile/src/{store,lib}/__tests__/*.test.ts` |

`docs/ASSUMPTIONS.md` (2026-08-06) "440 backend testi" rakamını veriyor — bu founder/önceki cycle beyanı, bu turda çalıştırılarak doğrulanmadı. Grep tabanlı 408 rakamıyla aynı büyüklük mertebesinde, fark muhtemelen `Theory`'lerin çalışma-zamanı çoğalması veya sayım anındaki commit farkından kaynaklanıyor — kesinleştirmek için `dotnet test --list-tests` çalıştırılmalı (bu oturumda `.sln` bulunamadığı için yapılamadı).

CI kapsamı (`docs/GAP-ANALYSIS.md` P1-8): Application test suite'inin CI'da koşup koşmadığı bu turda yeniden doğrulanmadı — GAP-ANALYSIS'teki P1-8 kaydına güvenilmeli, çelişki bulunmadı.

---

## 3. RFC Durumu (özet — detay `docs/rfc/README.md`)

| RFC | Konu | Status |
|---|---|---|
| RFC-001 | .NET 10 → .NET 8 LTS | Accepted (retrospektif, founder imzası bekliyor) |
| RFC-002 | Meilisearch yerine SQL filtreleme (MVP) | Accepted (retrospektif) |
| RFC-003 | JWT RS256 → HS256 | Accepted (retrospektif) |
| RFC-004 | Erzurum pilot | ✅ Accepted |
| RFC-005 | OTP-only launch | Superseded by RFC-010 |
| RFC-006 | Kapora %15 checkout | ✅ Accepted, aktivasyonu RFC-009 flag'ine bağlı |
| RFC-007 | Owner onboarding self-serve | ✅ Accepted, kapandı |
| RFC-008 | KVKK parçalı rıza akışı | ✅ Accepted, kapandı |
| RFC-009 | Ücretsiz büyüme modu (online ödeme flag) | ✅ Accepted |
| RFC-010 | Misafir gezinme + sosyal login | ✅ Accepted |
| RFC-011 | Web landing + public profil | ✅ Accepted |
| RFC-012 | Aylık ciro komisyonu | ✅ Accepted, implement edildi (bkz. §5) |
| RFC-013 | Per-shop kapora + dinamik kurallar | ✅ Accepted, implement edildi (bkz. §5) |

---

## 4. GAP-ANALYSIS.md Senkron Notu — Bu Turda Kodda Doğrulanan Kapanışlar

`docs/GAP-ANALYSIS.md` 2026-07-23 tarihli; bazı maddelere sonradan "✅ KAPANDI" güncellemesi düşülmüş (P0-5, P0-9, P0-10, P1-4, P1-6), bazılarına düşülmemiş. FAZ 1 bu turda **kod üzerinden yeniden doğrulanan** ama GAP-ANALYSIS.md metninde henüz kapanış notu OLMAYAN maddeler:

| Madde | GAP-ANALYSIS'teki iddia (2026-07-23) | Bu turda kod kanıtı | Durum |
|---|---|---|---|
| **P0-4** | "İptal → gerçek para iadesi otomasyonu yok" | `BookingCancelledEvent` → `BookingCancelledRefundHandler` → `ProcessBookingRefundJob` (Hangfire, `AutomaticRetry(5)`) → `BookingRefundService.RefundIfEligibleAsync` → PSP `RefundAsync`. Tam otonom zincir, kanıt: `docs/DOMAIN.md` §S2. | **Kapalı** (CLAUDE.md Cycle 9 notunda değişiklik olarak var ama GAP-ANALYSIS.md'de "✅ KAPANDI" satırı yok) |
| **P1-1** | "DB exclusion constraint yok, test yok" | `M15_BookingSlotExclusion` migration (`ex_bookings_stylist_slot`, `WHERE status IN (0,1,2)`) + `BookingSlotSafetyTests.cs` concurrent testleri (lock'lu + lock-bypass). Kanıt: `docs/DOMAIN.md` §2. | **Kapalı** |
| **P1-3** | "PendingTimeout job yok" | `ExpirePendingBookingsJob` (15dk cron, 3 dallı sweep, 30dk/randevu-saati eşiği) + müşteri başına 3 (global)/2 (per-shop) Pending limiti. Kanıt: `docs/DOMAIN.md` §2. | **Kapalı** |

**Öneri:** Bir sonraki GAP-ANALYSIS.md revizyonunda bu üç maddeye P0-9/P0-10 kalıbındaki gibi "✅ KAPANDI" güncelleme bloğu eklensin — bu PROGRESS.md dosyası o güncellemenin kanıt kaynağı olarak kullanılabilir.

**Bu turda hâlâ AÇIK doğrulanan (GAP-ANALYSIS ile çelişki yok, teyit):**
- **P1-5** (no-show zaman guard'ı yok) — `MarkNoShowCommandHandler` (`BookingActions.cs:180-199`) hâlâ yalnız sahiplik+status kontrolü yapıyor, `ScheduledAt` karşılaştırması yok. Owner sabah akşamki randevuyu iadesiz no-show işaretleyebilir — hâlâ mümkün.
- Diğer tüm P0/P1/P2 maddeleri bu turun kapsamında (12 domain sorusu + 4 doğrulama) yeniden taranmadı; GAP-ANALYSIS.md'nin kendi durumuna güvenilmeli.

---

## 5. Yeni Modül: Billing (RFC-012/013) — orijinal M1-M16 listesinde yok

`docs/04-modules/00-module-list.md`'deki M1-M16 envanterinde bir "Billing/Commission" modülü tanımlı değildi; RFC-012/013 ile kod tabanına girdi:

- `CommissionStatement` (`backend/src/Kuaforum.Domain/Modules/Billing/CommissionStatement.cs`): state machine `Open → Notified → Paid | Overdue → SuspensionApplied → Paid`, ayrıca `Dispute()` (owner itirazı).
- `CommissionPayment`: `Payment` aggregate'inden bağımsız "sibling" aggregate, yalnız `IPaymentProvider` arayüzünü paylaşıyor; bugün yalnız Mock provider ile test edilmiş.
- `SuspendOverdueCommissionAccountsJob` (günlük 03:00): gecikmiş hesapları `BarberShop.Suspend()` ile askıya alıyor — **mevcut randevulara dokunmuyor**, yalnız arama/keşiften kaldırıyor ve yeni randevu almayı engelliyor (bkz. `docs/DOMAIN.md` §S4, `docs/GAPS.md` G6).
- Web admin faturalama/tahsilat sayfası (`8a734d8`, `a1ae06d`) canlı.

**Öneri:** `docs/04-modules/00-module-list.md`'ye M17-Billing (veya benzeri) olarak eklenmesi — dokümantasyon-writer kapsamında ayrı bir görev, bu PROGRESS.md yalnız tespiti kaydediyor.

---

## 6. Modül Durumu — Kısa Güncelleme (detay `docs/GAP-ANALYSIS.md` §1)

GAP-ANALYSIS.md'nin M1-M16 tablosu (2026-07-23) hâlâ ana referans; bu FAZ 1 turunda doğrudan dokunulan/doğrulanan modüllerde not:

| Modül | GAP-ANALYSIS (2026-07-23) | FAZ 1 notu (2026-08-06) |
|---|---|---|
| M5 — Availability | 🟢 concurrency güvenliği eksik (P1-1 açık) | **P1-1 kapandı** — EXCLUDE constraint + concurrent test kanıtlı (§4) |
| M6 — Booking | 🟠 state machine tamam, güvenlik delikli | Refund otomasyonu (P0-4) + PendingTimeout (P1-3) kapandı; P1-5 (no-show guard) hâlâ açık; **yeni bulgular**: cross-shop çakışma yok (G1), WorkingHours değişince mevcut randevu kontrolü yok (G2) |
| M4 — Barber (Stylist alt-kırılımı) | 🟠 kısmi (KYC blok — kapandı) | **Yeni bulgular**: Stylist deaktivasyonu randevuları etkilemiyor (G3), owner=tek-çalışan otomatik stylist yaratmıyor (G5), `UserRole.Stylist` hâlâ hiç atanmıyor — kullanıcı hesabı/panel yok (G7, ASSUMPTIONS.md D2 doğrulandı) |
| M9 — Search | 🟠 kısmi | Backend free-text arama + TR normalizasyon hâlâ yok (ASSUMPTIONS.md D5 doğrulandı, yeni bulgu değil) |
| M11 — Notification | 🟠 kısmi/blok | Sessiz saat + günlük özet hâlâ yok (ASSUMPTIONS.md D6 doğrulandı); idempotency yalnız job-bazlı, merkezi değil (G8, `[unverified]`) |
| — (yeni) Billing | GAP-ANALYSIS'te yok | RFC-012/013 ile koda girdi, bkz. §5 |

---

## Bağlantılar

- `docs/DOMAIN.md` — bu özetin kanıt kaynağı (12 soru + 4 doğrulama)
- `docs/GAPS.md` — bu turda bulunan yeni boşluklar (G1-G9)
- `docs/GAP-ANALYSIS.md` — kapsamlı P0/P1/P2 envanteri (ana kaynak, burada kopyalanmadı)
- `docs/ASSUMPTIONS.md` — direktif ↔ proje eşlemesi
- `docs/decisions/ADR-001-direktif-uyarlama.md` — direktifin projeye uyarlanma kararı
- `docs/rfc/README.md` — RFC indeksi

**Son güncelleme:** 2026-08-06
