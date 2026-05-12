import { useEffect } from 'react';
import { Stack, useRouter, useSegments } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { useAuthStore } from '@/store/auth-store';
import { usePushNotifications } from '@/hooks/usePushNotifications';
import { AppErrorBoundary } from '@/components/AppErrorBoundary';

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
    const group = segments[0];
    const inAuthGroup = group === '(auth)';
    const inBarberGroup = group === '(barber)';
    const inCustomerGroup = group === '(tabs)';

    if (!isAuthenticated && !inAuthGroup) {
      router.replace('/(auth)/login');
      return;
    }

    if (isAuthenticated && inAuthGroup) {
      const role = user?.role ?? 'Customer';
      if (role === 'BarberOwner' || role === 'Stylist') {
        router.replace('/(barber)/today');
      } else {
        router.replace('/(tabs)');
      }
      return;
    }

    if (isAuthenticated && user) {
      const isBarber = user.role === 'BarberOwner' || user.role === 'Stylist';
      if (isBarber && inCustomerGroup) router.replace('/(barber)/today');
      else if (!isBarber && inBarberGroup) router.replace('/(tabs)');
    }
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
