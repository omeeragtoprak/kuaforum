import { Stack } from 'expo-router';

/**
 * RFC-007 owner onboarding sihirbazı (3 adım). Route grubu parantezsiz ("onboarding")
 * bilerek — AuthGate (app/_layout.tsx) yalnızca "(tabs)"/"(barber)" segmentlerine göre
 * yönlendirme yapıyor, bu grup her iki role da (Customer → BarberOwner terfisi süresince)
 * açık kalmalı.
 */
export default function OnboardingLayout() {
  return <Stack screenOptions={{ headerShown: false }} />;
}
