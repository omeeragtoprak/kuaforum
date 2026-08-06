# ADR-001 — Greenfield Otonom Geliştirme Direktifinin Mevcut Projeye Uyarlanması

**Tarih:** 2026-08-06
**Durum:** Accepted (uygulanmış, founder'ın direktifi verdiği bağlamla — direktifin genel çerçeve niyeti — tutarlı; kod-seviyesi kararlar tek tek `docs/rfc/`'de ayrıca onaylı)
**Karar sahibi:** Founder talimatıyla, Claude Code (documentation-writer) tarafından kayda geçirildi.

---

## Bağlam

Founder, greenfield (sıfırdan) bir proje için yazılmış bir "Otonom Geliştirme Direktifi" verdi (BÖLÜM A: proje tanımı doldurma, BÖLÜM D: domain/veri modeli kararları — D1 ödeme, D2 rol modeli, D3 randevu state machine, D4 slot/uygunluk motoru, D5 arama, D6 bildirim, D7 değerlendirme, D8 KVKK, BÖLÜM E: araç/skill kurulumu, BÖLÜM H: kapsam disiplini kuralları).

Ancak proje **greenfield değil**: FAZ 1 denetimi başladığında repo Cycle 12/13 seviyesindeydi —
- 13 kabul edilmiş RFC (`docs/rfc/RFC-001..013`, bkz. `docs/rfc/README.md`)
- 3 çalışan UI yüzeyi (mobile Expo, web-admin Next.js, web landing) gerçek backend endpoint'lerine bağlı
- ~408 backend test attribute'ü (grep sayımı, `docs/PROGRESS.md` §2) + 11 mobile jest test dosyası
- Çalışan bir Android test APK'sı (`ab5a9b5` "chore(build): APK çıktı dizini + kurulum notları")
- Kapsamlı bir mevcut gap envanteri (`docs/GAP-ANALYSIS.md`, 2026-07-23 tam tarama + nokta güncellemeleri)

Direktifi olduğu gibi (BÖLÜM A'yı sıfırdan doldurup BÖLÜM D kararlarını "verilecek karar" gibi ele alarak) uygulamak iki somut riske yol açardı:
1. **Var olan, founder-onaylı RFC kararlarının sessizce ezilmesi** — örn. direktif D1 "V2 gelir modeli = aylık sabit abonelik" derken, RFC-012 (founder onayı 2026-08-05) zaten "aylık ciro komisyonu" modelini implement etmiş durumda. Direktifi kör uygularsak RFC-012'yi geçersiz kılan bir "karar" fiilen alınmış olurdu — founder'a sorulmadan.
2. **Zaten yapılmış işin tekrarlanması** — direktifin BÖLÜM 0-2 (proje iskeleti, veri modeli, temel API) kurulum adımları zaten tamamlanmış; bunları "sıfırdan kurulum planı" gibi yeniden çalıştırmak hem zaman kaybı hem de mevcut 408 testlik güvenlik ağını riske atardı.

## Karar

Direktif, **sıfırdan kurulum planı olarak değil, denetim + tamamlama çerçevesi olarak** uygulandı. Somut olarak:

1. `docs/ASSUMPTIONS.md` oluşturuldu — direktifin BÖLÜM A'sını mevcut projenin gerçek stack/pazar/hedef bilgileriyle doldurdu ve direktif ↔ mevcut-durum eşlemesini madde madde (D1-D8) kayda geçirdi. Her madde üç kategoriden birine düştü: **Karşılandı** (kod zaten direktifin istediğini yapıyor), **Gerçek boşluk** (kod hiçbir şey yapmıyor, GAP listesine düşer), **Çelişki** (kod/RFC direktiften farklı bir karar almış, founder'a sorulması gerekir).
2. FAZ 1 (bu ADR'nin kapsamı), direktifin D2-D6 domain iddialarını **tahmin değil, kod okuyarak** doğruladı — sonuç `docs/DOMAIN.md` (12 soru + 4 doğrulama, dosya:satır kanıtlı) ve `docs/GAPS.md` (yeni bulunan boşluklar) olarak kayda geçti.
3. Var olan RFC'ler **otomatik geçersiz sayılmadı**. Direktifle çelişen yerler (örn. D1 abonelik vs RFC-012 komisyon, D3 `auto_confirm` flag'i vs RFC-013 "her randevuda kuaför onayı zorunlu") `docs/ASSUMPTIONS.md`'de "Çelişki → founder sorusu" olarak işaretlendi, susarak hiçbir yöne çözülmedi.
4. Direktifin istediği doküman iskeleti (`DOMAIN.md`, `PROGRESS.md`, `GAPS.md`, `decisions/`) üretildi, ama proje zaten olgun `docs/rfc/` (kararlar) ve `docs/GAP-ANALYSIS.md` (boşluklar) sahibiydi — yeni dosyalar bunların **yerine geçmedi**, onlara referans verdi; içerik kopyalanmadı (bkz. her dosyanın "Bağlantılar" bölümü).

## Gerekçe

- **CLAUDE.md §13 (Push-Back & Calibration Discipline)** ve proje kuralları ("her zaman dürüst, şüpheci, sorgulayıcı... founder yes-man değil mühendis ortağı istiyor") direktifi sessizce olduğu gibi uygulamayı değil, gerçekle karşılaştırıp farkı raporlamayı gerektiriyor.
- **CLAUDE.md §5 (Doküman Disiplini):** "Yeni karar gerektiren değişiklik için RFC" — direktifin kendisi bir "yeni karar" değil, bir **denetim çerçevesi**; ama direktifin BÖLÜM D maddelerinden proje gerçeğiyle çelişenler (D1, D3) birer RFC-seviyesi karar konusu, bu ADR o kararları vermedi, yalnız var olduğunu tespit edip founder'a havale etti.
- Direktifin kendi BÖLÜM H/4 kuralı ("sessizce kapsam daraltma yasak") burada simetrik olarak yorumlandı: **sessizce kapsam DEĞİŞTİRME de yasak** — RFC-012'yi direktif lehine sessizce iptal etmek, GAP-ANALYSIS.md'nin bulduğu gerçek boşlukları direktifin kendi madde numaralarıyla yeniden "keşfedilmiş" gibi sunmaktan farksız bir bilgi kaybı olurdu.
- Var olan test/CI/RFC altyapısını sıfırdan kurulum gibi ele almak, doğrudan **CLAUDE.md §13 madde 3** ("solo founder kapasitesi tehlikedeyse uyar") ihlaliydi — zaten kapanmış işi tekrar yaptırmak founder'ın en kıt kaynağı olan zamanı boşa harcardı.

## Sonuçlar

**Olumlu:**
- Direktifin domain-denetim niyeti (D2-D6'nın kodda gerçekten var olup olmadığını doğrulama) tam olarak yerine getirildi — 12 soru + 4 doğrulama, tahmin değil kanıt.
- Var olan RFC disiplini korundu; hiçbir founder-onaylı karar sessizce değiştirilmedi.
- Yeni bulgular (`docs/GAPS.md` G1-G9) mevcut GAP-ANALYSIS.md'nin tekrarı olmadan, ona ek olarak kaydedildi — bilgi kaybı yok, çift kayıt yok.

**Açık kalan / founder onayı bekleyen:**
- `docs/ASSUMPTIONS.md`'deki iki gerçek **çelişki** (D1 abonelik-vs-komisyon, D3 `auto_confirm` flag'i) bu ADR ile çözülmedi — founder'ın RFC-012/013'ü teyit etmesi veya direktifi RFC'ler lehine resmen revize etmesi gerekiyor.
- `docs/GAPS.md` G7 (Stylist kullanıcı tipi — gerçek bir kullanıcı hesabı olacak mı, yoksa konsept terk mi edilecek) founder kararı bekliyor; bu ADR bir yön önermiyor, yalnız kararın gerekliliğini kayda geçiriyor.
- Bir sonraki FAZ (varsa) bu ADR'nin kapsamı dışında — direktifin BÖLÜM D8+ (KVKK/güvenlik) ve BÖLÜM E-H maddeleri bu turda denetlenmedi; yalnız D2-D6 (12 domain sorusu) kapsamdaydı.

## Alternatifler (değerlendirilip reddedildi)

- **Direktifi olduğu gibi, greenfield kurulum planı olarak çalıştırmak.** Reddedildi — mevcut 13 RFC'yi ve 408 testlik altyapıyı riske atardı, founder onayı olmadan iş modeli kararlarını (RFC-012/013) fiilen geçersiz kılardı.
- **Direktifi tamamen görmezden gelmek, yalnız GAP-ANALYSIS.md'ye devam etmek.** Reddedildi — founder'ın direktifi bilinçli olarak verdiği, "denetim çerçevesi olarak kullan" talimatının kendisi görev tanımının parçası; direktifi yok saymak founder talimatını atlamak olurdu.
- **Direktif ↔ proje çelişkilerini (D1, D3) bu ADR içinde tek taraflı çözmek.** Reddedildi — CLAUDE.md §2 kuralı ("Kullanıcı sektörel veya ürün kararını 'sen ver' derse, gerekçeyle birlikte ver — sonra spec'e yaz") burada geçerli değil; founder byle bir yetki vermedi, yalnız direktifi denetim çerçevesi olarak uygulamayı istedi. Karar vermeden soru olarak taşımak daha güvenli.

---

## Bağlantılar

- `docs/ASSUMPTIONS.md` — direktif ↔ proje tam eşlemesi (bu ADR'nin dayandığı analiz)
- `docs/DOMAIN.md` — FAZ 1 domain denetimi (12 soru + 4 doğrulama)
- `docs/GAPS.md` — FAZ 1'de bulunan yeni boşluklar
- `docs/PROGRESS.md` — güncel durum özeti
- `docs/GAP-ANALYSIS.md` — kapsamlı mevcut gap envanteri
- `docs/rfc/README.md` — RFC indeksi ve statü tanımları

**Son güncelleme:** 2026-08-06
