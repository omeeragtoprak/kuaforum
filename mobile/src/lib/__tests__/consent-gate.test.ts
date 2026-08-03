import { resolvePostAuthRoute } from '@/lib/consent-gate';

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
});
