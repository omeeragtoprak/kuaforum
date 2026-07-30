import { resolveOnboardingStep, isValidShopName, isValidAddressLine } from '@/lib/onboarding';

describe('resolveOnboardingStep', () => {
  it('shop yok → adım 1 (salon bilgisi)', () => {
    expect(resolveOnboardingStep(null)).toBe(1);
  });

  it('shop var, Draft, adres yok → adım 2 (adres)', () => {
    expect(resolveOnboardingStep({ status: 0, addressLine1: null })).toBe(2);
    expect(resolveOnboardingStep({ status: 'Draft', addressLine1: '' })).toBe(2);
  });

  it('shop var, Draft, adres var → adım 3 (belgeler) — reddedilip Draft’a dönmüş başvuru da dahil', () => {
    expect(resolveOnboardingStep({ status: 0, addressLine1: 'Atatürk Cad. No:1' })).toBe(3);
    expect(resolveOnboardingStep({ status: 'Draft', addressLine1: 'Atatürk Cad. No:1' })).toBe(3);
  });

  it('shop var, Draft dışında (PendingKyc/Active/Suspended/Closed) → done (kyc durum ekranı)', () => {
    expect(resolveOnboardingStep({ status: 1, addressLine1: 'x'.repeat(10) })).toBe('done');
    expect(resolveOnboardingStep({ status: 'PendingKyc', addressLine1: 'x'.repeat(10) })).toBe('done');
    expect(resolveOnboardingStep({ status: 2, addressLine1: 'x'.repeat(10) })).toBe('done');
    expect(resolveOnboardingStep({ status: 'Active', addressLine1: 'x'.repeat(10) })).toBe('done');
    expect(resolveOnboardingStep({ status: 3, addressLine1: 'x'.repeat(10) })).toBe('done');
    expect(resolveOnboardingStep({ status: 4, addressLine1: 'x'.repeat(10) })).toBe('done');
  });
});

describe('isValidShopName', () => {
  it('2 karakterden kısa isim geçersiz', () => {
    expect(isValidShopName('A')).toBe(false);
    expect(isValidShopName('  ')).toBe(false);
  });

  it('2-120 karakter arası isim geçerli', () => {
    expect(isValidShopName('Ay')).toBe(true);
    expect(isValidShopName('Berber Ahmet Erkek Kuaförü')).toBe(true);
  });

  it('120 karakterden uzun isim geçersiz', () => {
    expect(isValidShopName('a'.repeat(121))).toBe(false);
  });

  it('baş/son boşluklar trim edilir', () => {
    expect(isValidShopName('  Ay  ')).toBe(true);
  });
});

describe('isValidAddressLine', () => {
  it('5 karakterden kısa adres geçersiz', () => {
    expect(isValidAddressLine('Cad.')).toBe(false);
  });

  it('5-200 karakter arası adres geçerli', () => {
    expect(isValidAddressLine('Atatürk Cad. No:1')).toBe(true);
  });

  it('200 karakterden uzun adres geçersiz (backend Line1 MaximumLength(200) ile paralel)', () => {
    expect(isValidAddressLine('a'.repeat(201))).toBe(false);
    expect(isValidAddressLine('a'.repeat(200))).toBe(true);
  });
});
