import { useEffect, useState } from 'react';
import { ActivityIndicator, View } from 'react-native';
import { Stack, useGlobalSearchParams, useRouter, useSegments } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { useAuthStore } from '@/store/auth-store';
import { usePushNotifications } from '@/hooks/usePushNotifications';
import { AppErrorBoundary } from '@/components/AppErrorBoundary';
import { isGuestAccessibleRoute, resolvePostAuthRoute, toHref } from '@/lib/consent-gate';
import { bootstrapApiBaseUrl } from '@/api/client';
import { Colors } from '@/lib/theme';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 60_000, retry: 2 },
  },
});

function AuthGate({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading, hydrate, user } = useAuthStore();
  const segments = useSegments();
  const router = useRouter();
  // RFC-010 — login/otp/consent zincirinin sonunda dönülecek hedef; login.tsx/otp.tsx/
  // consent.tsx explicit navigasyonla zaten taşıyor, burada AuthGate'in KENDİ reactive
  // fallback redirect'i (aşağıdaki "inAuthGroup || inConsentRoute" dalı) de aynı hedefi
  // hesaplayabilsin diye okunuyor — hangi mekanizma önce çalışırsa çalışsın sonuç aynı,
  // "boş tabs'a düşüp seçimleri kaybetme" riski kalmıyor. `useGlobalSearchParams` kökte
  // (root layout) bile o an eşleşen en derin rotanın TÜM query param'larını görür.
  const { redirectTo } = useGlobalSearchParams<{ redirectTo?: string }>();

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
      // RFC-010 — misafir gezinme: keşfet/arama/kuaför-detay + randevu onay ekranı
      // (kendi AuthPrompt'unu gösterir) login gerektirmez. Geri kalan korumalı
      // route'lar (booking detay/ödeme, (barber)/*, onboarding/*, account/*,
      // review/submit) klasik davranışta kalır — doğrudan login'e at.
      if (!isGuestAccessibleRoute(segments)) router.replace('/(auth)/login');
      return;
    }

    if (!user) return;

    // RFC-008 — güncel versiyonla ToS rızası yoksa (yeni kullanıcı OTP sonrası ya da
    // mevcut kullanıcının ilk açılışı) HERHANGİ bir rotadan /consent'e yönlendirilir;
    // onaylamadan (ya da çıkış yapmadan) uygulamaya geçilemez (KVKK m.5).
    if (user.requiresTosConsent) {
      // M-1 (review) — resolvePostAuthRoute üzerinden gidilir ki bu reactive fallback,
      // otp.tsx'in explicit navigasyonuyla yarışsa bile (hangisi önce çalışırsa çalışsın)
      // aynı `redirectTo`'yu /consent'in query'sine taşısın, kaybolmasın.
      if (!inConsentRoute && !inLegalRoute) router.replace(toHref(resolvePostAuthRoute(user, redirectTo)) as never);
      return;
    }

    if (inAuthGroup || inConsentRoute) {
      router.replace(toHref(resolvePostAuthRoute(user, redirectTo)) as never);
      return;
    }

    const isBarber = user.role === 'BarberOwner' || user.role === 'Stylist';
    if (isBarber && inCustomerGroup) router.replace('/(barber)/today');
    else if (!isBarber && inBarberGroup) router.replace('/(tabs)');
  }, [isAuthenticated, isLoading, segments, router, user, redirectTo]);

  return <>{children}</>;
}

/**
 * Bootstrap tamamlanana kadar (bkz. `bootstrapApiBaseUrl`) gösterilir — "beyaz ekran
 * yasak" kuralı gereği düz boş View değil, temalı arkaplan + spinner.
 */
function BootstrapLoading() {
  return (
    <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: Colors.background }}>
      <ActivityIndicator color={Colors.primary} size="large" accessibilityLabel="Uygulama başlatılıyor" />
    </View>
  );
}

export default function RootLayout() {
  // Test build'de kalıcı ayarda bir API adresi override'ı varsa, `apiClient`'ın
  // baseURL'i AuthGate mount olup `hydrate()` ilk isteği atmadan ÖNCE güncellenmeli —
  // aksi halde ilk istek hâlâ eski (build-time) adrese gider (2026-08-06 olayının kök
  // nedeni: LAN IP değişti, APK'daki adres ölüydü). Bootstrap resolve olana kadar
  // AuthGate/Stack hiç MOUNT EDİLMEZ, dolayısıyla hiçbir network isteği atılamaz.
  const [baseUrlReady, setBaseUrlReady] = useState(false);

  useEffect(() => {
    bootstrapApiBaseUrl().finally(() => setBaseUrlReady(true));
  }, []);

  return (
    <AppErrorBoundary>
      <GestureHandlerRootView style={{ flex: 1 }}>
        <SafeAreaProvider>
          <QueryClientProvider client={queryClient}>
            <StatusBar style="auto" />
            {baseUrlReady ? (
              <AuthGate>
                <Stack screenOptions={{ headerShown: false }} />
              </AuthGate>
            ) : (
              <BootstrapLoading />
            )}
          </QueryClientProvider>
        </SafeAreaProvider>
      </GestureHandlerRootView>
    </AppErrorBoundary>
  );
}
