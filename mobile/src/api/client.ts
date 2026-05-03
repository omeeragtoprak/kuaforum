import axios, { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from 'axios';
import Constants from 'expo-constants';
import { TokenStorage } from '@/lib/secure-storage';

const API_BASE_URL =
  process.env.EXPO_PUBLIC_API_BASE_URL ??
  (Constants.expoConfig?.extra as { apiBaseUrl?: string })?.apiBaseUrl ??
  'http://localhost:5000';

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
