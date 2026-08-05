import {
  resolvePostAuthRoute,
  isGuestAccessibleRoute,
  buildRedirectPath,
  sanitizeRedirectTo,
} from '@/lib/consent-gate';

describe('resolvePostAuthRoute', () => {
  it('requiresTosConsent true → /consent, rol fark etmez (customer)', () => {
    expect(resolvePostAuthRoute({ role: 'Customer', requiresTosConsent: true })).toBe('/consent');
  });

  it('requiresTosConsent true → /consent, rol fark etmez (barber owner)', () => {
    expect(resolvePostAuthRoute({ role: 'BarberOwner', requiresTosConsent: true })).toBe('/consent');
  });

  it('requiresTosConsent true → /consent, rol fark etmez (stylist)', () => {
    expect(resolvePostAuthRoute({ role: 'Stylist', requiresTosConsent: true })).toBe('/consent');
  });

  it('requiresTosConsent true → /consent, rol fark etmez (admin)', () => {
    expect(resolvePostAuthRoute({ role: 'Admin', requiresTosConsent: true })).toBe('/consent');
  });

  it('requiresTosConsent false + Customer → customer tabs', () => {
    expect(resolvePostAuthRoute({ role: 'Customer', requiresTosConsent: false })).toBe('/(tabs)');
  });

  it('requiresTosConsent false + Admin → customer tabs (barber değil)', () => {
    expect(resolvePostAuthRoute({ role: 'Admin', requiresTosConsent: false })).toBe('/(tabs)');
  });

  it('requiresTosConsent false + BarberOwner → barber paneli', () => {
    expect(resolvePostAuthRoute({ role: 'BarberOwner', requiresTosConsent: false })).toBe('/(barber)/today');
  });

  it('requiresTosConsent false + Stylist → barber paneli', () => {
    expect(resolvePostAuthRoute({ role: 'Stylist', requiresTosConsent: false })).toBe('/(barber)/today');
  });

  it('requiresTosConsent undefined (eski/güncellenmemiş user objesi) → rol-bazlı rota, hata fırlatmaz', () => {
    expect(resolvePostAuthRoute({ role: 'Customer' })).toBe('/(tabs)');
    expect(resolvePostAuthRoute({ role: 'BarberOwner' })).toBe('/(barber)/today');
  });

  // RFC-010 — misafir randevu kapısı: redirectTo threading
  it('redirectTo verilmemiş → eski davranış değişmez (regresyon)', () => {
    expect(resolvePostAuthRoute({ role: 'Customer', requiresTosConsent: false })).toBe('/(tabs)');
  });

  it('consent gerekmiyor + redirectTo verilmiş → doğrudan redirectTo hedefine gidilir', () => {
    expect(
      resolvePostAuthRoute(
        { role: 'Customer', requiresTosConsent: false },
        '/booking/confirm?stylistId=abc',
      ),
    ).toBe('/booking/confirm?stylistId=abc');
  });

  it('consent gerekiyor + redirectTo verilmiş → ÖNCE /consent, redirectTo query olarak taşınır', () => {
    expect(
      resolvePostAuthRoute(
        { role: 'Customer', requiresTosConsent: true },
        '/booking/confirm?stylistId=abc',
      ),
    ).toBe('/consent?redirectTo=%2Fbooking%2Fconfirm%3FstylistId%3Dabc');
  });

  it('consent gerekiyor + redirectTo verilmiş → barber owner için de önce consent (rol fark etmez)', () => {
    expect(
      resolvePostAuthRoute({ role: 'BarberOwner', requiresTosConsent: true }, '/booking/confirm'),
    ).toBe('/consent?redirectTo=%2Fbooking%2Fconfirm');
  });

  it('redirectTo null → undefined ile aynı davranır (regresyon, eski call site\'lar bozulmaz)', () => {
    expect(resolvePostAuthRoute({ role: 'Customer', requiresTosConsent: false }, null)).toBe('/(tabs)');
  });
});

describe('isGuestAccessibleRoute — RFC-010 misafir gezinme allow-list', () => {
  it('(auth) grubu misafire açık (login/otp ekranları)', () => {
    expect(isGuestAccessibleRoute(['(auth)', 'login'])).toBe(true);
    expect(isGuestAccessibleRoute(['(auth)', 'otp'])).toBe(true);
  });

  it('legal route misafire açık (RFC-008 — login öncesi de okunabilir)', () => {
    expect(isGuestAccessibleRoute(['legal', 'kullanim-kosullari'])).toBe(true);
  });

  it('consent route misafire açık — /consent deep-link\'i unauthenticated iken de render olur (submit zaten 401 alır, kabul edilen davranış)', () => {
    expect(isGuestAccessibleRoute(['consent'])).toBe(true);
  });

  it('(tabs) grubunun TAMAMI serbest — hangi sekmenin AuthPrompt göstereceğine ekran karar verir', () => {
    expect(isGuestAccessibleRoute(['(tabs)', 'index'])).toBe(true);
    expect(isGuestAccessibleRoute(['(tabs)', 'search'])).toBe(true);
    expect(isGuestAccessibleRoute(['(tabs)', 'bookings'])).toBe(true);
    expect(isGuestAccessibleRoute(['(tabs)', 'notifications'])).toBe(true);
    expect(isGuestAccessibleRoute(['(tabs)', 'profile'])).toBe(true);
  });

  it('kuaför detay (barber/[id]) misafire açık', () => {
    expect(isGuestAccessibleRoute(['barber', 'abc-123'])).toBe(true);
  });

  it('booking/confirm misafire açık (kendi AuthPrompt\'unu gösterir, seçimler redirectTo\'da taşınır)', () => {
    expect(isGuestAccessibleRoute(['booking', 'confirm'])).toBe(true);
  });

  it('booking/[id] (detay) ve booking/payment misafire KAPALI — klasik login redirect', () => {
    expect(isGuestAccessibleRoute(['booking', 'abc-123'])).toBe(false);
    expect(isGuestAccessibleRoute(['booking', 'payment'])).toBe(false);
  });

  it('(barber) owner paneli misafire KAPALI', () => {
    expect(isGuestAccessibleRoute(['(barber)', 'today'])).toBe(false);
  });

  it('onboarding, account, review/submit misafire KAPALI', () => {
    expect(isGuestAccessibleRoute(['onboarding', 'shop'])).toBe(false);
    expect(isGuestAccessibleRoute(['account', 'edit-profile'])).toBe(false);
    expect(isGuestAccessibleRoute(['review', 'submit'])).toBe(false);
  });
});

describe('buildRedirectPath', () => {
  it('tüm değerler tanımlıysa pathname + sıralı query string üretir', () => {
    expect(buildRedirectPath('/booking/confirm', { stylistId: 'abc', scheduledAt: '2026-08-10T10:00:00Z' })).toBe(
      '/booking/confirm?stylistId=abc&scheduledAt=2026-08-10T10%3A00%3A00Z',
    );
  });

  it('undefined/boş değerler atlanır', () => {
    expect(
      buildRedirectPath('/booking/confirm', { stylistId: 'abc', stylistName: undefined, shopName: '' }),
    ).toBe('/booking/confirm?stylistId=abc');
  });

  it('hiç param yoksa çıplak pathname döner (? eklenmez)', () => {
    expect(buildRedirectPath('/(tabs)/bookings', {})).toBe('/(tabs)/bookings');
  });

  it('özel karakterler (Türkçe, boşluk, &) encode edilir', () => {
    expect(buildRedirectPath('/booking/confirm', { shopName: 'Ahmet & Oğulları Kuaför' })).toBe(
      '/booking/confirm?shopName=Ahmet%20%26%20O%C4%9Fullar%C4%B1%20Kuaf%C3%B6r',
    );
  });
});

// I-1 (review, güvenlik) — open-redirect: `redirectTo` deep-link'ten (kuaforum://login?
// redirectTo=https%3A%2F%2Fevil.example) gelen, kullanıcı kontrolündeki bir string.
// `router.replace`/`push`'a mutlak bir URL/scheme verilirse expo-router bunu
// `Linking.openURL`'e yönlendirip harici bir sayfa/uygulama açabiliyor.
describe('sanitizeRedirectTo — I-1 open-redirect guard', () => {
  it('mutlak https:// URL reddedilir → null, resolvePostAuthRoute varsayılan rotaya düşer', () => {
    expect(sanitizeRedirectTo('https://evil.example')).toBeNull();
    expect(
      resolvePostAuthRoute({ role: 'Customer', requiresTosConsent: false }, 'https://evil.example'),
    ).toBe('/(tabs)');
  });

  it('protocol-relative //host reddedilir → null (tarayıcı/Linking bunu da mutlak URL sayar)', () => {
    expect(sanitizeRedirectTo('//evil.example')).toBeNull();
  });

  it('uygulama-içi path (/ ile başlayan, // ile başlamayan) kabul edilir, aynen döner', () => {
    expect(sanitizeRedirectTo('/booking/confirm?x=1')).toBe('/booking/confirm?x=1');
  });
});
