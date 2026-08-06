import axios, { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from 'axios';
import { Platform } from 'react-native';
import Constants from 'expo-constants';
import { TokenStorage } from '@/lib/secure-storage';
import {
  resolveInitialBaseUrl,
  setPersistedBaseUrlOverride,
  clearPersistedBaseUrlOverride,
} from '@/lib/runtime-base-url';

// Backend dev port is 5100. Android emulator can't reach host's localhost — needs 10.0.2.2.
// iOS simulator and web share host networking. Override with EXPO_PUBLIC_API_BASE_URL on
// physical devices (set to your dev machine's LAN IP, e.g. http://192.168.1.x:5100).
const DEFAULT_DEV_BASE_URL = Platform.select({
  android: 'http://10.0.2.2:5100',
  ios: 'http://localhost:5100',
  default: 'http://localhost:5100',
});

// Derleme anındaki (build-time) varsayılan — APK'ya gömülür, değiştirilemez.
// Test build'lerde runtime override bunun ÜSTÜNE gelir (bkz. bootstrapApiBaseUrl).
const BUILD_DEFAULT_BASE_URL =
  process.env.EXPO_PUBLIC_API_BASE_URL ??
  (Constants.expoConfig?.extra as { apiBaseUrl?: string })?.apiBaseUrl ??
  DEFAULT_DEV_BASE_URL;

export const apiClient: AxiosInstance = axios.create({
  // Bootstrap tamamlanana kadar (bkz. bootstrapApiBaseUrl) build-time varsayılanı
  // kullanılır; app/_layout.tsx bootstrap bitmeden hiçbir isteğin atılmadığını
  // garanti eder (AuthGate/hydrate() mount edilmez), o yüzden burada bir "geçici
  // yanlış adres" penceresi yoktur.
  baseURL: BUILD_DEFAULT_BASE_URL,
  timeout: 15_000,
  headers: { 'Content-Type': 'application/json' },
});

/**
 * App açılışında (ilk istek atılmadan / `hydrate()` çağrılmadan ÖNCE) TAM OLARAK BİR
 * KEZ çağrılmalı — `app/_layout.tsx`'teki `RootLayout` bu promise resolve olana kadar
 * `AuthGate`'i (dolayısıyla `hydrate()`'i) mount etmeyerek yarış koşulunu engeller.
 * Test build'de kalıcı ayarda bir override varsa `apiClient.defaults.baseURL`'i onunla
 * değiştirir; yoksa (veya production'da) build-time varsayılanında kalır.
 */
export async function bootstrapApiBaseUrl(): Promise<void> {
  const resolved = await resolveInitialBaseUrl(BUILD_DEFAULT_BASE_URL);
  apiClient.defaults.baseURL = resolved;
}

/** O anki (bootstrap sonrası, override edilmiş olabilecek) aktif taban adres. */
export function getApiBaseUrl(): string {
  return (apiClient.defaults.baseURL as string | undefined) ?? BUILD_DEFAULT_BASE_URL;
}

/** APK'ya gömülü değişmez varsayılan — "Varsayılana dön" kısayolunda gösterilir/kullanılır. */
export function getBuildDefaultApiBaseUrl(): string {
  return BUILD_DEFAULT_BASE_URL;
}

/**
 * Test build ekranından (server-settings.tsx) çağrılır: kalıcı ayara yazar VE
 * axios instance'ın baseURL'ini anında günceller (uygulamayı yeniden başlatmaya
 * gerek yok). Production'da `setPersistedBaseUrlOverride` sessiz no-op olduğu için
 * yalnızca in-memory `apiClient.defaults.baseURL` değişir — ama bu fonksiyona zaten
 * production'da erişilebilecek bir UI yolu yok (bkz. server-settings.tsx guard'ı).
 */
export async function setApiBaseUrlOverride(url: string): Promise<void> {
  await setPersistedBaseUrlOverride(url);
  apiClient.defaults.baseURL = url;
}

/** Kalıcı override'ı temizler ve axios instance'ı build-time varsayılanına döndürür. */
export async function resetApiBaseUrlToDefault(): Promise<void> {
  await clearPersistedBaseUrlOverride();
  apiClient.defaults.baseURL = BUILD_DEFAULT_BASE_URL;
}

apiClient.interceptors.request.use(async (config: InternalAxiosRequestConfig) => {
  const token = await TokenStorage.getAccessToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

let refreshPromise: Promise<boolean> | null = null;

apiClient.interceptors.response.use(
  (r) => r,
  async (error: AxiosError) => {
    const original = error.config as InternalAxiosRequestConfig & { _retry?: boolean };

    if (error.response?.status === 401 && !original._retry && !original.url?.includes('/auth/')) {
      original._retry = true;

      if (!refreshPromise) {
        refreshPromise = refreshTokens();
      }
      const refreshed = await refreshPromise;
      refreshPromise = null;

      if (refreshed) {
        const newToken = await TokenStorage.getAccessToken();
        if (newToken && original.headers) {
          original.headers.Authorization = `Bearer ${newToken}`;
        }
        return apiClient(original);
      }

      await TokenStorage.clear();
      // Lazy import to avoid circular dependency between auth-store ↔ api/client.
      // Refresh failed → tokens wiped → flip Zustand state so AuthGate redirects to login.
      try {
        const { useAuthStore } = await import('@/store/auth-store');
        useAuthStore.getState().clearLocalAuth();
      } catch { /* best-effort */ }
    }

    return Promise.reject(error);
  },
);

/**
 * Refresh access+refresh token çifti. Interceptor 401'de otomatik çağırır;
 * RFC-007 owner onboarding Adım 1'de de (Customer→BarberOwner rol terfisi
 * sonrası eski token'ın role claim'i bayat kaldığından) EXPLICIT olarak
 * dışarıdan çağrılmak üzere export edildi. İmza değişmedi.
 */
export async function refreshTokens(): Promise<boolean> {
  try {
    const refreshToken = await TokenStorage.getRefreshToken();
    const deviceFingerprint = await TokenStorage.getDeviceFingerprint();
    if (!refreshToken) return false;

    const res = await axios.post(
      // getApiBaseUrl() — sabit değil: kullanıcı test build'de adres değiştirdiyse
      // refresh de YENİ adrese gitmeli (raw axios.post kullanılıyor çünkü apiClient
      // instance'ının response interceptor'ı burada özyinelemeli 401 döngüsüne yol açar).
      `${getApiBaseUrl()}/v1/auth/refresh`,
      { refreshToken, deviceFingerprint },
      { timeout: 10_000 },
    );

    if (res.data?.accessToken && res.data?.refreshToken) {
      await TokenStorage.setAccessToken(res.data.accessToken);
      await TokenStorage.setRefreshToken(res.data.refreshToken);
      return true;
    }
    return false;
  } catch (err) {
    const { ErrorReporter } = await import('@/lib/error-reporter');
    ErrorReporter.capture(err, { tag: 'auth.refresh', extra: { failed: true } });
    return false;
  }
}

export type ApiError = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
};

export function extractApiError(err: unknown): string {
  const axErr = err as AxiosError<ApiError>;
  if (axErr.response?.data) {
    const d = axErr.response.data;
    if (d.detail) return d.detail;
    if (d.title) return d.title;
  }
  if (axErr.message) return axErr.message;
  return 'Bilinmeyen hata.';
}
