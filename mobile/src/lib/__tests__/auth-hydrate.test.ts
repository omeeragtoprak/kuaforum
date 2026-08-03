import { shouldClearTokensOnHydrateError } from '../auth-hydrate';

// RFC-008 kapanış — hydrate token-clear daraltması: yalnız sunucunun token'ı
// gerçekten reddettiği durumlarda (401/403) local token temizlenir; network,
// timeout, 5xx gibi geçici hatalarda token korunur (sessiz logout regresyonu pin'i).
describe('shouldClearTokensOnHydrateError', () => {
  it('401 (token geçersiz/expired) → temizle', () => {
    expect(shouldClearTokensOnHydrateError(401)).toBe(true);
  });

  it('403 (token reddedildi) → temizle', () => {
    expect(shouldClearTokensOnHydrateError(403)).toBe(true);
  });

  it('status yok (network hatası / timeout) → token KORU', () => {
    expect(shouldClearTokensOnHydrateError(undefined)).toBe(false);
  });

  it('500 (sunucu hatası) → token KORU', () => {
    expect(shouldClearTokensOnHydrateError(500)).toBe(false);
  });

  it('503 (geçici kesinti) → token KORU', () => {
    expect(shouldClearTokensOnHydrateError(503)).toBe(false);
  });

  it('404 (beklenmedik ama auth-reddi değil) → token KORU', () => {
    expect(shouldClearTokensOnHydrateError(404)).toBe(false);
  });

  it('0 (bazı client hatalarında sahte status) → token KORU', () => {
    expect(shouldClearTokensOnHydrateError(0)).toBe(false);
  });
});
