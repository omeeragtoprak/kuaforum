import { Platform } from 'react-native';

/**
 * KUAFORUM TASARIM SİSTEMİ — "Vitrin" (founder seçimi, 2026-08-11)
 * =================================================================
 * Karar: saf beyaz zemin, neredeyse siyah tipografi, rengi KUAFÖRÜN İŞİ getirir.
 * Marka rengi yok denecek kadar az — uygulama bir vitrin camı gibi, içeriği gösterir.
 *
 * ÖNCEKİ SÜRÜMDEN NEDEN AYRILDIK: eski palet mor (#7C3AED) + mor→lila degrade +
 * emoji sekme ikonlarına dayanıyordu. Bu birleşim "yapay zekâ üretimi" izlenimi
 * veren en tanınmış kalıplardan biri ve founder'ın "minimal, Apple gibi" isteğiyle
 * çelişiyordu. Degrade ve emoji ikonlar TAMAMEN kaldırıldı; ikonlar çizgi (stroke)
 * SVG olacak.
 *
 * UYGULAMA KURALLARI
 * - Ekranlarda ham hex YAZMA. Her renk buradan gelir; yeni bir tona ihtiyaç varsa
 *   önce buraya eklenir. (Halen 9 ekranda ham hex var — dönüşümde temizleniyor.)
 * - Vurgu rengi (`accent`) YALNIZ yıkıcı olmayan tek bir eylemi işaretlemek için.
 *   Birincil buton `ink` (siyah) zemindir; accent'i butona kaymayın.
 * - Durum renkleri (success/warning/error) vurgu renginden AYRIDIR ve marka rengi
 *   sayılmaz; yalnız durum bildirir.
 */

export const Colors = {
  // --- Zeminler (saf beyazdan başlar; gri tonlar hafif soğuk biasla seçildi,
  //     nötr gri "seçilmemiş" görünüyor) ---
  background: '#FFFFFF',
  surface: '#FFFFFF',
  surfaceAlt: '#F2F3F5',   // dolgu: arama kutusu, pasif çip, iskelet
  surfaceSunken: '#FAFAFB',
  border: '#EEF0F2',
  borderStrong: '#DDE0E4',
  divider: '#EEF0F2',

  // --- Mürekkep ---
  ink: '#16181C',          // birincil metin + birincil buton zemini
  text: '#16181C',
  textSecondary: '#5A616B',
  textMuted: '#8A9099',
  textInverse: '#FFFFFF',

  // --- Vurgu: bakır. Berber aletlerinin (jilet, makas sapı) rengi.
  //     Beyaz üzerinde okunur, mor gibi "teknoloji ürünü" hissi vermez.
  //     Kullanım: seçili sekme altı çizgisi, bağlantı, tek bir öne çıkan rozet. ---
  accent: '#C96F4A',
  accentSoft: '#FBEDE7',
  accentInk: '#8F4A2E',    // accentSoft üzerinde metin için (kontrast ≥ 4.5:1)

  // --- Durum (vurgudan bağımsız) ---
  success: '#1E7F53',
  successSoft: '#E6F4EC',
  warning: '#9A6A12',
  warningSoft: '#FBF1DC',
  error: '#B3261E',
  errorSoft: '#FBEAE9',
  info: '#1F5F8B',
  infoSoft: '#E7F0F7',

  // --- Monogram paleti: fotoğrafı OLMAYAN salon için.
  //     "Vitrin" yönünün bilinen zayıflığı buydu (fotoğraf yoksa ekran boşalır).
  //     Çözüm: salon adının baş harfleri + adından türetilen sabit bir ton.
  //     Tonlar bilerek düşük doygunlukta — fotoğrafın yerini almaya çalışmaz,
  //     boşluğu sakince doldurur. ---
  monogram: [
    { bg: '#EDE7E1', fg: '#6B5B4E' },
    { bg: '#E4E9EC', fg: '#4F5E68' },
    { bg: '#E9E6EC', fg: '#5E5668' },
    { bg: '#E6ECE7', fg: '#4E6353' },
    { bg: '#F0E9E3', fg: '#6E5B4A' },
    { bg: '#E7EAF0', fg: '#535C6E' },
  ] as const,

  star: '#E0A32E',
  overlay: 'rgba(22, 24, 28, 0.42)',

  // --- Switch (Switch bileşeninin native `trackColor.false`'u) — 3 ekranda ham hex
  //     '#E5E7EB' olarak tekrarlanıyordu (booking-policy anahtarları). Kapalı bir
  //     kontrolün nötr zemini; border tonlarından ayrı tutuluyor çünkü anlamı
  //     "çizgi" değil "dolgu" — ileride tek yerden değiştirilebilsin diye buraya taşındı. ---
  switchTrackOff: '#E5E7EB',

  /** @deprecated Rol degradeleri kaldırıldı — "Vitrin"de degrade yok. */
  roleCustomer: ['#16181C', '#16181C'] as const,
  /** @deprecated */
  roleBarber: ['#16181C', '#16181C'] as const,
  /** @deprecated */
  roleAdmin: ['#16181C', '#16181C'] as const,
  /** @deprecated Eski mor marka rengi — kalan kullanımlar temizlenecek. */
  primary: '#16181C',
  /** @deprecated */
  primaryLight: '#5A616B',
  /** @deprecated */
  primaryDark: '#16181C',
  /** @deprecated */
  primarySoft: '#F2F3F5',
};

/** Salon adından sabit bir monogram tonu seçer (aynı salon hep aynı renk). */
export function monogramFor(name: string): { bg: string; fg: string; initials: string } {
  const clean = (name ?? '').trim();
  let hash = 0;
  for (let i = 0; i < clean.length; i++) hash = (hash * 31 + clean.charCodeAt(i)) >>> 0;
  const tone = Colors.monogram[hash % Colors.monogram.length];
  const words = clean.split(/\s+/).filter(Boolean);
  const initials = (words.length >= 2
    ? words[0][0] + words[1][0]
    : clean.slice(0, 2)
  ).toLocaleUpperCase('tr-TR');
  return { ...tone, initials };
}

export const Spacing = {
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  xxl: 32,
  xxxl: 48,
};

/**
 * Yarıçap: "her yerde aynı 12px" görünümünden kaçınmak için kademeli.
 * Küçük öğeler daha keskin, büyük yüzeyler daha yumuşak — Apple'ın sürekli
 * eğrilik (continuous corner) hissine yaklaşır.
 */
export const Radius = {
  xs: 6,
  sm: 9,
  md: 11,
  lg: 16,
  xl: 22,
  full: 999,
};

/**
 * Tip ölçeği. Başlıklarda sıkı harf aralığı (negatif tracking) — Apple'ın
 * büyük başlık karakteri. Gövde metni nötr aralıkta kalır.
 */
export const FontSize = {
  xs: 11,
  sm: 13,
  md: 15,
  lg: 17,
  xl: 20,
  xxl: 24,
  display: 30,
};

export const LetterSpacing = {
  display: -0.8,
  title: -0.4,
  body: 0,
  label: 0.4,   // küçük büyük-harf etiketler
};

export const FontWeight = {
  regular: '400' as const,
  medium: '500' as const,
  semibold: '600' as const,
  bold: '700' as const,
};

/**
 * Gölge: "Vitrin"de gölge neredeyse yok. Ayrım gölgeyle değil ÇİZGİYLE ve
 * BOŞLUKLA yapılır. `sm` yalnız yüzen öğeler (alt aksiyon çubuğu, modal) için.
 */
export const Shadow = {
  none: {},
  sm: Platform.select({
    ios: { shadowColor: '#16181C', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 2 } },
    android: { elevation: 2 },
    default: {},
  }),
  md: Platform.select({
    ios: { shadowColor: '#16181C', shadowOpacity: 0.07, shadowRadius: 16, shadowOffset: { width: 0, height: 6 } },
    android: { elevation: 4 },
    default: {},
  }),
  lg: Platform.select({
    ios: { shadowColor: '#16181C', shadowOpacity: 0.10, shadowRadius: 24, shadowOffset: { width: 0, height: 10 } },
    android: { elevation: 8 },
    default: {},
  }),
};

export const Layout = {
  screenPaddingX: Spacing.lg,
  maxContentWidth: 520,
  tapTargetMin: 44,
};
