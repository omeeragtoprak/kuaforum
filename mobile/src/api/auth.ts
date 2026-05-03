import { apiClient } from './client';
import { TokenStorage } from '@/lib/secure-storage';

export type SendOtpResponse = {
  otpId: string;
  expiresAt: string;
};

export type VerifyOtpResponse = {
  accessToken: string;
  refreshToken: string;
  accessExpiresAt: string;
  refreshExpiresAt: string;
  user: {
    id: string;
    phone?: string | null;
    displayName?: string | null;
    role: 'Customer' | 'BarberOwner' | 'Stylist' | 'Admin';
  };
};

export const AuthApi = {
  async sendOtp(phone: string, purpose: 'Register' | 'Login' = 'Login'): Promise<SendOtpResponse> {
    const res = await apiClient.post<SendOtpResponse>('/v1/auth/otp/send', { phone, purpose });
    return res.data;
  },

  async verifyOtp(otpId: string, code: string): Promise<VerifyOtpResponse> {
    const deviceFingerprint = await TokenStorage.getDeviceFingerprint();
    const res = await apiClient.post<VerifyOtpResponse>('/v1/auth/otp/verify', {
      otpId,
      code,
      deviceFingerprint,
      deviceName: 'Mobile (Expo)',
    });
    if (res.data.accessToken && res.data.refreshToken) {
      await TokenStorage.setAccessToken(res.data.accessToken);
      await TokenStorage.setRefreshToken(res.data.refreshToken);
    }
    return res.data;
  },

  async logout(): Promise<void> {
    const refreshToken = await TokenStorage.getRefreshToken();
    if (refreshToken) {
      try {
        await apiClient.post('/v1/auth/logout', { refreshToken });
      } catch {
        // ignore — clear local anyway
      }
    }
    await TokenStorage.clear();
  },

  async getMyProfile() {
    const res = await apiClient.get('/v1/users/me');
    return res.data;
  },
};
