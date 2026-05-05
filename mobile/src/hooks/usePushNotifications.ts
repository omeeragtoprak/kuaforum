import { useEffect, useRef } from 'react';
import { Platform } from 'react-native';
import Constants, { ExecutionEnvironment } from 'expo-constants';
import { DeviceTokenApi } from '@/api/device-token';
import { useAuthStore } from '@/store/auth-store';

/**
 * Setup foreground/background notification handling and register the device's FCM/APNs token
 * with the backend on login. Requires a real device + EAS-built standalone (Expo Go from
 * SDK 53 onward does NOT deliver remote push, and importing expo-notifications statically
 * crashes Expo Go's bridge). We guard execution at the runtime + module-load level.
 */
export function usePushNotifications() {
  const isAuthed = useAuthStore((s) => s.isAuthenticated);
  const fcmTokenRef = useRef<string | null>(null);

  // Only run on builds where expo-notifications is supported.
  // Expo Go = 'storeClient' (push removed in SDK 53). Standalone/dev-client = 'standalone' or 'bare'.
  const isStandalone = Constants.executionEnvironment !== ExecutionEnvironment.StoreClient;

  useEffect(() => {
    if (!isStandalone || !isAuthed) return;
    let cancelled = false;

    (async () => {
      try {
        // Lazy-load expo-notifications + expo-device so Expo Go never resolves the modules.
        const Notifications = await import('expo-notifications');
        const Device = await import('expo-device');

        Notifications.setNotificationHandler({
          handleNotification: async () => ({
            shouldShowAlert: true,
            shouldPlaySound: true,
            shouldSetBadge: true,
            shouldShowBanner: true,
            shouldShowList: true,
          }),
        });

        if (!Device.isDevice) {
          console.log('[push] simulator/emulator detected; skipping registration.');
          return;
        }

        const settings = await Notifications.getPermissionsAsync();
        let finalStatus = settings.status;
        if (finalStatus !== 'granted') {
          const req = await Notifications.requestPermissionsAsync();
          finalStatus = req.status;
        }
        if (finalStatus !== 'granted') {
          console.log('[push] permission denied; skipping registration.');
          return;
        }

        const tokenResp = await Notifications.getDevicePushTokenAsync();
        if (cancelled) return;
        const token = tokenResp.data;
        if (!token) return;
        fcmTokenRef.current = token;
        await DeviceTokenApi.register({
          fcmToken: token,
          platform: Platform.OS === 'ios' ? 'Ios' : Platform.OS === 'android' ? 'Android' : 'Web',
          appVersion: Constants.expoConfig?.version ?? undefined,
          deviceModel: Device.modelName ?? undefined,
        });
        console.log('[push] registered token len=', token.length);
      } catch (err) {
        console.warn('[push] registration skipped:', err instanceof Error ? err.message : err);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [isAuthed, isStandalone]);

  // On logout, unregister the previously registered token.
  useEffect(() => {
    if (isAuthed) return;
    const t = fcmTokenRef.current;
    if (!t) return;
    DeviceTokenApi.unregister(t).catch(() => { /* best-effort */ });
    fcmTokenRef.current = null;
  }, [isAuthed]);
}
