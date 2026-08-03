import { useEffect } from 'react';
import { Stack, useRouter, useSegments } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { useAuthStore } from '@/store/auth-store';
import { usePushNotifications } from '@/hooks/usePushNotifications';
import { AppErrorBoundary } from '@/components/AppErrorBoundary';
import { resolvePostAuthRoute } from '@/lib/consent-gate';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 60_000, retry: 2 },
  },
});

function AuthGate({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading, hydrate, user } = useAuthStore();
  const segments = useSegments();
  const router = useRouter();

  useEffect(() => {
    hydrate();
  }, [hydrate]);

  usePushNotifications();

  useEffect(() => {
    if (isLoading) return;
    // `as string` — expo-router'ın (dev-only, gitignored) `.expo/types/router.d.ts`
    // typed-routes union'ı yeni eklenen 'legal'/'consent' segmentlerini henüz
    // içermiyor (yalnızca `expo start` regen eder); mevcut kod tabanında da aynı
    // sebeple dinamik route'lar `as never`/geniş tipe cast ediliyor (bkz. profile.tsx).
    const group = segments[0] as string;
    const inAuthGroup = group === '(auth)';
    const inBarberGroup = group === '(barber)';
    const inCustomerGroup = group === '(tabs)';
    // RFC-008 — /legal/* AllowAnonymous (login öncesi de okunabilir); /consent hem
    // giriş öncesi anlamsız hem de aşağıdaki rıza gate'inin hedefi — ikisi de gate
    // kontrolünün DIŞINDA kalmalı, aksi halde sonsuz redirect döngüsü oluşur.
    const inLegalRoute = group === 'legal';
    const inConsentRoute = group === 'consent';

    if (!isAuthenticated) {
      if (!inAuthGroup && !inLegalRoute) router.replace('/(auth)/login');
      return;
    }

    if (!user) return;

    // RFC-008 — güncel versiyonla ToS rızası yoksa (yeni kullanıcı OTP sonrası ya da
    // mevcut kullanıcının ilk açılışı) HERHANGİ bir rotadan /consent'e yönlendirilir;
    // onaylamadan (ya da çıkış yapmadan) uygulamaya geçilemez (KVKK m.5).
    if (user.requiresTosConsent) {
      if (!inConsentRoute && !inLegalRoute) router.replace('/consent' as never);
      return;
    }

    if (inAuthGroup || inConsentRoute) {
      router.replace(resolvePostAuthRoute(user) as never);
      return;
    }

    const isBarber = user.role === 'BarberOwner' || user.role === 'Stylist';
    if (isBarber && inCustomerGroup) router.replace('/(barber)/today');
    else if (!isBarber && inBarberGroup) router.replace('/(tabs)');
  }, [isAuthenticated, isLoading, segments, router, user]);

  return <>{children}</>;
}

export default function RootLayout() {
  return (
    <AppErrorBoundary>
      <GestureHandlerRootView style={{ flex: 1 }}>
        <SafeAreaProvider>
          <QueryClientProvider client={queryClient}>
            <StatusBar style="auto" />
            <AuthGate>
              <Stack screenOptions={{ headerShown: false }} />
            </AuthGate>
          </QueryClientProvider>
        </SafeAreaProvider>
      </GestureHandlerRootView>
    </AppErrorBoundary>
  );
}
