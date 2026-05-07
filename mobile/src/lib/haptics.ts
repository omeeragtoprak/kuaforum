import { Vibration, Platform } from 'react-native';

/**
 * Lightweight haptic feedback using RN built-in Vibration API. No extra dep.
 * Pattern intentions:
 * - light:    quick acknowledge (button press confirm)
 * - success:  positive confirmation (booking created, payment OK)
 * - warning:  caution (about-to-cancel, about-to-no-show)
 * - error:    failure (validation rejected, server error)
 *
 * Web/SSR no-ops cleanly because Vibration is a no-op there.
 */
export const Haptics = {
  light() {
    if (Platform.OS === 'ios') Vibration.vibrate(10); // iOS only fires for ≥1ms
    else Vibration.vibrate(15);
  },
  success() {
    Vibration.vibrate(Platform.OS === 'ios' ? 30 : [0, 30, 60, 30]);
  },
  warning() {
    Vibration.vibrate(Platform.OS === 'ios' ? 50 : [0, 50, 100, 50]);
  },
  error() {
    Vibration.vibrate(Platform.OS === 'ios' ? 80 : [0, 60, 80, 60, 80, 60]);
  },
};
