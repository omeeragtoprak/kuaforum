import axios, { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from 'axios';
import { Platform } from 'react-native';
import Constants from 'expo-constants';
import { TokenStorage } from '@/lib/secure-storage';

// Backend dev port is 5100. Android emulator can't reach host's localhost — needs 10.0.2.2.
// iOS simulator and web share host networking. Override with EXPO_PUBLIC_API_BASE_URL on
// physical devices (set to your dev machine's LAN IP, e.g. http://192.168.1.x:5100).
const DEFAULT_DEV_BASE_URL = Platform.select({
  android: 'http://10.0.2.2:5100',
  ios: 'http://localhost:5100',
  default: 'http://localhost:5100',
});

const API_BASE_URL =
  process.env.EXPO_PUBLIC_API_BASE_URL ??
  (Constants.expoConfig?.extra as { apiBaseUrl?: string })?.apiBaseUrl ??
  DEFAULT_DEV_BASE_URL;

export const apiClient: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: 15_000,
  headers: { 'Content-Type': 'application/json' },
});

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

async function refreshTokens(): Promise<boolean> {
  try {
    const refreshToken = await TokenStorage.getRefreshToken();
    const deviceFingerprint = await TokenStorage.getDeviceFingerprint();
    if (!refreshToken) return false;

    const res = await axios.post(
      `${API_BASE_URL}/v1/auth/refresh`,
      { refreshToken, deviceFingerprint },
      { timeout: 10_000 },
    );

    if (res.data?.accessToken && res.data?.refreshToken) {
      await TokenStorage.setAccessToken(res.data.accessToken);
      await TokenStorage.setRefreshToken(res.data.refreshToken);
      return true;
    }
    return false;
  } catch {
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
