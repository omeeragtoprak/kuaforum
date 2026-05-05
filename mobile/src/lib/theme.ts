import { Platform } from 'react-native';

export const Colors = {
  // Brand
  primary: '#7C3AED',
  primaryLight: '#A78BFA',
  primaryDark: '#5B21B6',
  primarySoft: '#EDE9FE',

  accent: '#F59E0B',
  accentSoft: '#FEF3C7',

  // Surfaces
  background: '#F8FAFC',
  surface: '#FFFFFF',
  surfaceAlt: '#F1F5F9',
  border: '#E2E8F0',
  borderStrong: '#CBD5E1',
  divider: '#F1F5F9',

  // Text
  text: '#0F172A',
  textSecondary: '#475569',
  textMuted: '#94A3B8',
  textInverse: '#FFFFFF',

  // Status
  success: '#10B981',
  successSoft: '#D1FAE5',
  warning: '#F59E0B',
  warningSoft: '#FEF3C7',
  error: '#EF4444',
  errorSoft: '#FEE2E2',
  info: '#3B82F6',
  infoSoft: '#DBEAFE',

  // Role gradients (start, end)
  roleCustomer: ['#7C3AED', '#A78BFA'] as const,
  roleBarber: ['#0EA5E9', '#38BDF8'] as const,
  roleAdmin: ['#0F172A', '#475569'] as const,

  // Misc
  star: '#FACC15',
  overlay: 'rgba(15, 23, 42, 0.45)',
};

export const Spacing = {
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  xxl: 32,
  xxxl: 48,
};

export const Radius = {
  sm: 8,
  md: 14,
  lg: 20,
  xl: 28,
  full: 999,
};

export const FontSize = {
  xs: 12,
  sm: 13,
  md: 15,
  lg: 17,
  xl: 20,
  xxl: 26,
  display: 34,
};

export const FontWeight = {
  regular: '400' as const,
  medium: '500' as const,
  semibold: '600' as const,
  bold: '700' as const,
};

export const Shadow = {
  sm: Platform.select({
    ios: { shadowColor: '#0F172A', shadowOpacity: 0.06, shadowRadius: 4, shadowOffset: { width: 0, height: 1 } },
    android: { elevation: 1 },
    default: {},
  }),
  md: Platform.select({
    ios: { shadowColor: '#0F172A', shadowOpacity: 0.08, shadowRadius: 10, shadowOffset: { width: 0, height: 4 } },
    android: { elevation: 3 },
    default: {},
  }),
  lg: Platform.select({
    ios: { shadowColor: '#0F172A', shadowOpacity: 0.12, shadowRadius: 18, shadowOffset: { width: 0, height: 8 } },
    android: { elevation: 6 },
    default: {},
  }),
};

export const Layout = {
  screenPaddingX: Spacing.lg,
  maxContentWidth: 520,
  tapTargetMin: 44,
};
