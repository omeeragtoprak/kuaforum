export type ConsentGateRole = 'Customer' | 'BarberOwner' | 'Stylist' | 'Admin';

export type ConsentGateUser = {
  role: ConsentGateRole;
  requiresTosConsent?: boolean;
};

/**
 * RFC-008 — tek doğruluk kaynağı için tek gate: hem `app/_layout.tsx`'teki AuthGate'in
 * reactive redirect'i hem OTP-verify/dev-login sonrası explicit navigasyon bu saf
 * fonksiyonu çağırır. `requiresTosConsent` true ise rol fark etmeksizin `/consent`'e
 * (KVKK m.5 açık rıza olmadan uygulamaya geçilemez); değilse mevcut rol-bazlı hedefe
 * (barber paneli / customer tabs) — davranış RFC-006/007'deki mevcut rol yönlendirmesiyle
 * birebir aynı, buraya sadece taşındı.
 */
export function resolvePostAuthRoute(user: ConsentGateUser): string {
  if (user.requiresTosConsent) return '/consent';
  const isBarber = user.role === 'BarberOwner' || user.role === 'Stylist';
  return isBarber ? '/(barber)/today' : '/(tabs)';
}
