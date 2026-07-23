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
| [RFC-005](RFC-005-otp-only-launch.md) | MVP Launch'ta Yalnız Telefon+OTP Girişi | ✅ Accepted (founder onayı 2026-07-23) | 2026-07-23 |

---

## Not

RFC-001 ila RFC-004, `docs/GAP-ANALYSIS.md`'nin tespit ettiği ve zaten kod tabanında fiilen uygulanmış sapmaları retrospektif olarak belgeler — "Accepted" statüsü kodun bugün o şekilde çalıştığını gösterir, founder'ın bu sapmaları **onayladığı** anlamına gelmez. Founder incelemesi sonrası her biri ya olduğu gibi onaylanır (statü değişmez, yalnızca imza eklenir) ya da bir sonraki RFC ile geri alınır/değiştirilir (`Superseded by RFC-NNN`).

RFC-005 farklı: henüz uygulanmamış bir öneridir, kod tabanını yansıtmaz — onaylanmadan bağlayıcı değildir.

---

Son güncelleme: 2026-07-23
