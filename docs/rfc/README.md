# RFC İndeksi

`docs/rfc/`, `PHASE-0-MASTER-SPEC.md`'de kilitlenen kararlardan sapma gerektiren her değişikliğin belgelendiği yerdir — kod önce değişip sonra RFC yazılmışsa bile (retrospektif), sapma burada iz bırakır. Yeni bir RFC `RFC-NNN-<konu>.md` adıyla açılır; `Status: Proposed` ile başlar, founder onayıyla `Accepted` olur, karardan dönülürse `Superseded`/`Rejected` olarak işaretlenir, geçersiz kılan RFC'ye link verilir. Her modül/mimari kararı sadece bir kez kilitlenir — aynı konuda ikinci bir RFC açılırsa öncekine `Superseded by RFC-NNN` notu düşülür, silinmez.

---

## İndeks

| No | Başlık | Status | Tarih |
|---|---|---|---|
| [RFC-001](RFC-001-dotnet8-retarget.md) | Backend Target Framework: .NET 10 → .NET 8 LTS | Accepted (retrospektif — founder imzası bekliyor) | 2026-07-23 |
| [RFC-002](RFC-002-sql-search-mvp.md) | Arama: Meilisearch Yerine Yapısal SQL Filtreleme (MVP) | Accepted (retrospektif — founder imzası bekliyor) | 2026-07-23 |
| [RFC-003](RFC-003-jwt-hs256.md) | JWT İmzalama: RS256 Yerine HS256 | Accepted (retrospektif — founder imzası bekliyor) | 2026-07-23 |
| [RFC-004](RFC-004-erzurum-pilot.md) | Fiili Pilot Şehir: Erzurum | ✅ Accepted (founder onayı 2026-07-23 — "şimdilik Erzurum", 81 il açık) | 2026-07-23 |
| [RFC-005](RFC-005-otp-only-launch.md) | MVP Launch'ta Yalnız Telefon+OTP Girişi | Superseded by RFC-010 (2026-08-05) — OTP korunuyor, tekel kalktı | 2026-07-23 |
| [RFC-006](RFC-006-kapora-checkout.md) | Ödeme Modeli: Kapora %15 + Tam Ödeme + Salonda Kalan | ✅ Accepted (founder onayı 2026-07-24) — aktivasyonu RFC-009 flag'ine bağlı | 2026-07-29 |
| [RFC-007](RFC-007-owner-onboarding.md) | Owner Onboarding: Self-Serve Rol Geçişi + Mobile Sihirbaz | ✅ Accepted (founder onayı 2026-07-30) | 2026-07-30 |
| [RFC-008](RFC-008-kvkk-consent-flow.md) | KVKK Aydınlatma + Parçalı Rıza Akışı | ✅ Accepted (founder onayı 2026-07-31) | 2026-08-03 |
| [RFC-009](RFC-009-free-growth-mode.md) | Ücretsiz Büyüme Modu: Online Ödeme Feature-Flag'i | ✅ Accepted (founder onayı 2026-08-05) | 2026-08-05 |
| [RFC-010](RFC-010-guest-browsing-social-login.md) | Misafir Gezinme + Sosyal Giriş (Google/Apple) Genişlemesi | ✅ Accepted (founder onayı 2026-08-05) | 2026-08-05 |
| [RFC-011](RFC-011-web-landing-barber-profiles.md) | Web Landing + Kuaför Public Profil Sayfaları | ✅ Accepted (founder onayı 2026-08-05) | 2026-08-05 |

---

## Not

RFC-001 ila RFC-004, `docs/GAP-ANALYSIS.md`'nin tespit ettiği ve zaten kod tabanında fiilen uygulanmış sapmaları retrospektif olarak belgeler — "Accepted" statüsü kodun bugün o şekilde çalıştığını gösterir, founder'ın bu sapmaları **onayladığı** anlamına gelmez. Founder incelemesi sonrası her biri ya olduğu gibi onaylanır (statü değişmez, yalnızca imza eklenir) ya da bir sonraki RFC ile geri alınır/değiştirilir (`Superseded by RFC-NNN`).

RFC-005, 2026-07-23'te onaylanıp uygulanmıştı (yalnız telefon+OTP); 2026-08-05'te RFC-010 ile **superseded** oldu — kayıt yöntemi tekeli kalktı, OTP üç yöntemden biri olarak korundu. RFC-005'in Guideline 4.8 bulgusu RFC-010'un doğrudan dayanağı, ayrıca geçerli.

RFC-006'nın kapora+komisyon+webhook mimarisi 2026-08-05'te RFC-009 ile **askıya alındı** (süperseden değil) — launch'ta `Payment:OnlinePaymentsEnabled=false` flag'iyle kapalı, kod aynen duruyor, aktivasyonu flag'e bağlı.

---

Son güncelleme: 2026-08-05
