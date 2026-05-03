# UI / UX Tasarım Prensipleri

> Modern, güvenilir, şeffaf, hızlı. Türk müşteri + kuaför davranışlarına özel optimizasyon.

---

## 1. Marka Hissiyatı

- **Modern + sıcak.** Steril değil. Salonun atmosferinden bir parça.
- **Türk** — yerli simgeler, doğal Türkçe (yapay çeviri değil).
- **Güvenilir** — fiyat şeffaf, iade kuralı görünür, KVKK rıza net.

---

## 2. Renk Paleti (Phase 1 başında final)

- **Primary:** Sıcak ton (mor/koyu turuncu adayları — 2 hafta evaluation).
- **Secondary:** Nötr koyu gri (arka plan).
- **Accent:** Hızlı eylem (CTA) için.
- **Semantic:** success/warning/error/info renkleri Tailwind/Tamagui standardına yakın.
- **Tema:** Dark mode V1.5'tan itibaren (MVP'de light only).

---

## 3. Typography

- **Font:** Inter veya geometric sans-serif (Tamagui ile uyumlu).
- **Türkçe karakter desteği** zorunlu (ı, İ, ğ, ş, ç, ö, ü).
- **Hiyerarşi:** Display 32px → H1 24px → H2 20px → H3 18px → Body 16px → Small 14px.
- **Font scale** kullanıcı tercihine uy.

---

## 4. Layout & Spacing

- **8px grid** — tüm paddings/margins 8'in katı.
- **Mobile-first.** Web admin desktop responsive — ama mobile da çalışır.
- **Safe area** iOS notch + Android status bar dikkat.
- **Bottom tab navigation** mobile (4-5 ana ekran).

---

## 5. UX Prensipleri

### 5.1 5 Dakika Vaadi
- Müşteri tarafında **first booking 5 dakikada** olmalı:
  - Kayıt: 60s (telefon + OTP).
  - Keşif: 60s (filtre + bir kuaför seç).
  - Slot seçimi: 30s.
  - Ödeme: 90s (3DS dahil).
  - **Toplam: ~4 dk.**

### 5.2 5 Saniye Kuaför Eylemi
- Bildirim → kabul/reddet 5 saniyede.
- Push'tan ana ekran → randevu detay 1 tap.
- "Boşum" toggle ana ekran üst köşe — 1 tap.

### 5.3 Şeffaflık
- Fiyat **randevu öncesi** net.
- Süre **randevu öncesi** net.
- İade kuralı **ödeme öncesi** ekranda.
- KVKK **rıza ekranda** parçalı toggle.

### 5.4 Empty / Loading / Error State
Her async aksiyonda 4 state:
1. **Initial:** anlamlı placeholder.
2. **Loading:** skeleton (spinner değil).
3. **Empty:** "Henüz randevu yok" + suggested action.
4. **Error:** retry button + sebep.

### 5.5 Geri Dönüşler (Feedback)
- Her tap haptic feedback (iOS).
- Success → toast veya inline confirmation.
- Hata → modal değil, inline + retry.

### 5.6 Erişilebilirlik
- WCAG 2.1 AA minimum.
- Screen reader label her interactive element.
- Tap target min 44pt.
- Color-only information YASAK.

---

## 6. UI Library Karar (Phase 1 başında)

**Adaylar:**

### Tamagui
- ✅ Compile-time optimization, multi-platform.
- ✅ Modern, theme-able, dark mode hazır.
- ✅ Animation ergonomik.
- ❌ Learning curve daha steep.

### React Native Paper
- ✅ Material Design 3 olgun.
- ✅ Hızlı başlangıç.
- ❌ "Material" hissi — Türk salon estetiğine az yakışır.

**Karar:** 1 hafta hands-on test, Phase 1 başlangıcında final.

---

## 7. Navigation Pattern

### Mobile (Customer App)
```
Bottom Tab:
- 🏠 Ana Sayfa (keşif + harita)
- 📅 Randevularım
- ❤️ Favoriler
- 👤 Profil
```

### Mobile (Barber Panel)
```
Bottom Tab:
- 📋 Bugün
- 📅 Takvim
- 🛠 Hizmetler
- 💰 Gelir
- 👤 Profil
```

### Web (Admin)
```
Sidebar:
- Dashboard
- Kullanıcılar
- Kuaförler
- KYC Kuyruk
- Randevular
- Ödeme & İade
- Yorum Moderasyon
- Audit Log
- Sistem
```

---

## 8. Component Library

Phase 1'de kurulacak shared component library:
- `Button` (primary, secondary, ghost, destructive, sizes)
- `Input` (text, phone, otp, password, search)
- `Card` (barber, booking, service)
- `BottomSheet` (action selector)
- `Modal` (confirm, info, error)
- `Calendar` (slot picker)
- `Map` (Google Maps wrapper)
- `Badge` (rating, walk-in, verified)
- `Avatar` (user, salon)
- `EmptyState`, `LoadingSkeleton`, `ErrorState`

---

## 9. Mikro İnteraksiyonlar

- Slot seçildi → haptic + scale animasyon.
- Randevu onaylandı → confetti veya benzeri positive feedback.
- Kuaför "boşum" toggle → status indicator (yeşil pulsing dot).
- Rating yıldız → tap'te dolma animasyonu.

---

## 10. Türkiye-Spesifik UX Detaylar

- Hatasız Türkçe metin (yapay çeviri yasak).
- Kadın/erkek/unisex salon ikon ayrımı.
- Mahalle ismi büyük + ilçe küçük (kullanıcı zihinsel pattern).
- Para formatı `₺` simgesi, `1.234,56 ₺`.
- Telefon formatı `+90 5•• ••• •• ••`.

---

## 11. Phase 1+ UI/UX Çıktıları

- Wireframe (Figma): 30+ ekran (customer + barber).
- Prototip akışı: 3 ana journey.
- Component library inventory.
- Style guide PDF.
- Brand book (logo, renk, tipografi).

---

## Bağlantılar

- [`docs/PHASE-0-MASTER-SPEC.md`](../PHASE-0-MASTER-SPEC.md)
- [`docs/02-requirements/02-personas.md`](../02-requirements/02-personas.md)
- [`docs/06-flows/01-user-journeys.md`](../06-flows/01-user-journeys.md)
