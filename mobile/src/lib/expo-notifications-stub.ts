// Stub used in Expo Go (push removed from Expo Go SDK 53+).
// Real expo-notifications is loaded only in standalone/EAS builds via Metro resolver.
const Unsupported = () => {
  throw new Error('[push] expo-notifications is not available in Expo Go. Build a dev-client or standalone app.');
};

export const setNotificationHandler = (_: unknown) => {};
export const getPermissionsAsync = async () => ({ status: 'denied' as const });
export const requestPermissionsAsync = async () => ({ status: 'denied' as const });
export const getDevicePushTokenAsync = async () => ({ data: '' });
export const addNotificationReceivedListener = (_: unknown) => ({ remove: () => {} });
export const addNotificationResponseReceivedListener = (_: unknown) => ({ remove: () => {} });
export const removeNotificationSubscription = (_: unknown) => {};
export default { Unsupported };
