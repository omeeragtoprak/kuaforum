import { apiClient } from './client';

export type DevicePlatform = 'Ios' | 'Android' | 'Web';

export const DeviceTokenApi = {
  async register(input: {
    fcmToken: string;
    platform: DevicePlatform;
    appVersion?: string;
    deviceModel?: string;
  }): Promise<void> {
    await apiClient.post('/v1/users/me/device-tokens', input);
  },
  async unregister(fcmToken: string): Promise<void> {
    await apiClient.delete('/v1/users/me/device-tokens', { params: { fcmToken } });
  },
};
