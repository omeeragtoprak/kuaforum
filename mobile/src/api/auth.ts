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

  async devLogin(role: 'Customer' | 'BarberOwner' | 'BarberOwnerDraft' | 'Admin'): Promise<VerifyOtpResponse> {
    const deviceFingerprint = await TokenStorage.getDeviceFingerprint();
    const res = await apiClient.post<VerifyOtpResponse>('/v1/auth/dev-login', {
      role,
      deviceFingerprint,
      deviceName: 'Expo Dev Quick Login',
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

  async updateMyProfile(body: { displayName?: string | null; avatarUrl?: string | null }): Promise<void> {
    await apiClient.patch('/v1/users/me', body);
  },

  async deleteMyAccount(): Promise<void> {
    await apiClient.delete('/v1/users/me');
  },

  async restoreMyAccount(): Promise<void> {
    await apiClient.post('/v1/users/me/restore');
  },

  async getMySessions(): Promise<Session[]> {
    const res = await apiClient.get<Session[]>('/v1/users/me/sessions');
    return res.data;
  },

  async revokeSession(sessionId: string): Promise<void> {
    await apiClient.delete(`/v1/users/me/sessions/${sessionId}`);
  },

  async getMyConsents(): Promise<Consent[]> {
    const res = await apiClient.get<Consent[]>('/v1/users/me/consents');
    return res.data;
  },

  async updateConsent(purpose: ConsentPurposeKey, grant: boolean): Promise<void> {
    await apiClient.patch('/v1/users/me/consents', { purpose, grant });
  },
};

export type Session = {
  id: string;
  deviceFingerprint: string;
  deviceName: string | null;
  createdAt: string;
  expiresAt: string;
  lastUsedAt: string | null;
  lastUsedIp: string | null;
  isCurrent: boolean;
};

export type ConsentPurposeKey = 'TermsOfService' | 'Marketing' | 'Location' | 'HealthData';

export type Consent = {
  /** Backend serializes enum as string when JsonStringEnumConverter active. */
  purpose: ConsentPurposeKey | number;
  granted: boolean;
  version: string;
  grantedAt: string;
  revokedAt: string | null;
};
