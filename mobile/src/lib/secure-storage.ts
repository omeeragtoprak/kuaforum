import * as SecureStore from 'expo-secure-store';

const ACCESS_TOKEN_KEY = 'kuaforum.access_token';
const REFRESH_TOKEN_KEY = 'kuaforum.refresh_token';
const DEVICE_FINGERPRINT_KEY = 'kuaforum.device_fingerprint';

export const TokenStorage = {
  async getAccessToken(): Promise<string | null> {
    return SecureStore.getItemAsync(ACCESS_TOKEN_KEY);
  },
  async setAccessToken(token: string): Promise<void> {
    await SecureStore.setItemAsync(ACCESS_TOKEN_KEY, token);
  },
  async getRefreshToken(): Promise<string | null> {
    return SecureStore.getItemAsync(REFRESH_TOKEN_KEY);
  },
  async setRefreshToken(token: string): Promise<void> {
    await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, token);
  },
  async getDeviceFingerprint(): Promise<string> {
    let fp = await SecureStore.getItemAsync(DEVICE_FINGERPRINT_KEY);
    if (!fp) {
      fp = `dev-${Date.now()}-${Math.random().toString(36).slice(2)}`;
      await SecureStore.setItemAsync(DEVICE_FINGERPRINT_KEY, fp);
    }
    return fp;
  },
  async clear(): Promise<void> {
    await SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY);
    await SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY);
  },
};
