import { resolveOnboardingStep } from '../onboarding';

/**
 * Kuaför panelindeki "kurulum durumu" kartının karar mantığı.
 *
 * NEDEN BU TEST VAR (emülatör bulgusu, 2026-08-12): "Yeni kuaför (Draft)" hızlı girişiyle
 * girildiğinde panel 0 randevu / ₺0 ciro gösteriyordu ve ekranda DURUMU AÇIKLAYAN HİÇBİR
 * ŞEY YOKTU. Sihirbaza dönmenin yolu da yoktu — `resolveOnboardingStep` yalnız onboarding
 * ekranının kendi içinde çağrılıyordu, yani oraya zaten gitmiş olmak gerekiyordu. Pratikte
 * yeni kaydolan her kuaför boş bir panele çarpıp tıkanıyordu.
 *
 * Burada kilitlenen: hangi salon durumunun sihirbaza, hangisinin "bekleniyor" durumuna
 * düştüğü. `today.tsx` bu iki yardımcıyı birleştirir; ayrıştırma mantığının kendisi
 * saf olduğu için burada test edilir.
 */
describe('salon kurulum durumu → panel uyarısı', () => {
  it('salon hiç yoksa sihirbazın 1. adımına yönlendirir', () => {
    expect(resolveOnboardingStep(null)).toBe(1);
  });

  it('Draft + adres yok → adres adımı', () => {
    expect(resolveOnboardingStep({ status: 0, addressLine1: null })).toBe(2);
    expect(resolveOnboardingStep({ status: 'Draft', addressLine1: '   ' })).toBe(2);
  });

  it('Draft + adres var → belge adımı (reddedilip Draft\'a dönenler de buraya düşer)', () => {
    expect(resolveOnboardingStep({ status: 0, addressLine1: 'Atatürk Cd. No:5' })).toBe(3);
  });

  it('Draft DIŞINDAKİ statüler sihirbaza düşmez', () => {
    // PendingKyc / Active / Suspended: sihirbaz bitmiş demektir. Panel bunları
    // ayrı ayrı ele alır (inceleniyor / normal / askıda) ama hiçbiri "kurulum yarım" değildir.
    expect(resolveOnboardingStep({ status: 1, addressLine1: 'x' })).toBe('done');
    expect(resolveOnboardingStep({ status: 2, addressLine1: 'x' })).toBe('done');
    expect(resolveOnboardingStep({ status: 'PendingKyc', addressLine1: 'x' })).toBe('done');
  });

  it('status hem sayı hem metin gelebilir — ikisi de aynı sonucu vermeli', () => {
    // Backend int VEYA JsonStringEnumConverter ile string gönderebiliyor; panelin
    // durum ayrımı bu yüzden String()'e çevirerek karşılaştırır.
    expect(resolveOnboardingStep({ status: 0, addressLine1: null }))
      .toBe(resolveOnboardingStep({ status: '0', addressLine1: null }));
  });
});
