import { toHref, resolvePostAuthRoute } from '../consent-gate';

/**
 * Giriş sonrası yönlendirmenin query parametrelerini KAYBETMEMESİ.
 *
 * NEDEN BU TEST VAR (emülatör bulgusu, 2026-08-12): misafir bir kuaförde slot seçip
 * "Devam et" deyince AuthPrompt çıkıyor, giriş yapılınca randevu onay ekranına
 * dönülmesi gerekiyordu. Bunun yerine uygulama ÇÖKÜYORDU:
 * `RangeError: Invalid time value` — çünkü ekran `formatDateTime(params.scheduledAt!)`
 * çağırıyor ve `scheduledAt` ulaşmıyordu. `router.replace(<string>)` ile verilen
 * tek parçalı path'in query'si güvenilir biçimde taşınmıyordu.
 *
 * Bu, RFC-010'un tam kalbindeki akış (misafirin randevu alması). Sessizce geri
 * gelebilecek bir hata olduğu için nesne dönüşümü burada sabitleniyor.
 */
describe('toHref', () => {
  it('query içeren path\'i pathname + params nesnesine ayırır', () => {
    const href = toHref('/booking/confirm?barberShopId=abc&scheduledAt=2026-08-12T10:00:00Z');
    expect(href).toEqual({
      pathname: '/booking/confirm',
      params: { barberShopId: 'abc', scheduledAt: '2026-08-12T10:00:00Z' },
    });
  });

  it('query yoksa path\'i olduğu gibi bırakır (gereksiz sarmalama yok)', () => {
    expect(toHref('/(tabs)')).toBe('/(tabs)');
    expect(toHref('/(barber)/today')).toBe('/(barber)/today');
  });

  it('yüzde-kodlu değerleri çözer', () => {
    const href = toHref('/booking/confirm?shopName=Da%C4%9F%20Erkek%20Kuaf%C3%B6r%C3%BC');
    expect(href).toEqual({
      pathname: '/booking/confirm',
      params: { shopName: 'Dağ Erkek Kuaförü' },
    });
  });

  it('bozuk yüzde-kodlamasında ÇÖKMEZ, ham değeri taşır', () => {
    // Eksik/garip bir parametre yüzünden navigasyonun tamamen başarısız olması,
    // kullanıcı için o parametreyi ham taşımaktan daha kötüdür.
    const href = toHref('/booking/confirm?bad=%E0%A4%A');
    expect(href).toEqual({ pathname: '/booking/confirm', params: { bad: '%E0%A4%A' } });
  });

  it('değersiz anahtarı boş string yapar', () => {
    expect(toHref('/x?flag')).toEqual({ pathname: '/x', params: { flag: '' } });
  });

  it('resolvePostAuthRoute çıktısıyla birlikte parametreleri korur (asıl senaryo)', () => {
    const user = { role: 'Customer', requiresTosConsent: false } as const;
    const redirect = '/booking/confirm?barberShopId=s1&scheduledAt=2026-08-12T10:00:00Z&totalAmount=14000';

    const href = toHref(resolvePostAuthRoute(user, redirect));

    expect(href).toEqual({
      pathname: '/booking/confirm',
      params: {
        barberShopId: 's1',
        scheduledAt: '2026-08-12T10:00:00Z',
        totalAmount: '14000',
      },
    });
  });

  it('rıza gerekiyorsa consent rotasına gider ve redirectTo\'yu parametre olarak taşır', () => {
    const user = { role: 'Customer', requiresTosConsent: true } as const;
    const href = toHref(resolvePostAuthRoute(user, '/booking/confirm?scheduledAt=X'));

    expect(href).toEqual({
      pathname: '/consent',
      params: { redirectTo: '/booking/confirm?scheduledAt=X' },
    });
  });
});
