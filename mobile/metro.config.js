// Learn more: https://docs.expo.dev/guides/customizing-metro/
const { getDefaultConfig } = require('expo/metro-config');
const path = require('path');

const config = getDefaultConfig(__dirname);

// Expo Go (SDK 53+) ships without expo-notifications native APIs. Statically importing
// the package crashes the bundler ("Unable to resolve ./presentNotificationAsync").
// In dev (Expo Go) we redirect expo-notifications imports to a local stub so the
// bundle still builds. In production / EAS / dev-client, set EAS_BUILD=true (or use a
// dev-client) to load the real module.
const useRealNotifications =
  process.env.EAS_BUILD === 'true' || process.env.EXPO_USE_REAL_NOTIFICATIONS === 'true';

if (!useRealNotifications) {
  const stub = path.resolve(__dirname, 'src/lib/expo-notifications-stub.ts');
  const originalResolver = config.resolver.resolveRequest;
  config.resolver.resolveRequest = (context, moduleName, platform) => {
    if (moduleName === 'expo-notifications') {
      return { type: 'sourceFile', filePath: stub };
    }
    return originalResolver
      ? originalResolver(context, moduleName, platform)
      : context.resolveRequest(context, moduleName, platform);
  };
}

module.exports = config;
