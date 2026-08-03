import { create } from 'zustand';
import { TokenStorage } from '@/lib/secure-storage';
import { AuthApi } from '@/api/auth';

export type AuthRole = 'Customer' | 'BarberOwner' | 'Stylist' | 'Admin';

export type AuthUser = {
  id: string;
  phone?: string | null;
  displayName?: string | null;
  email?: string | null;
  role: AuthRole;
  /** RFC-008 — server-derived; sadece `hydrate()` üzerinden (`GET /v1/users/me`) gelir.
   * OTP-verify/dev-login response'unda YOK — o yüzden login sonrası da hydrate() çağrılır. */
  requiresTosConsent?: boolean;
};

type AuthState = {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  setUser: (user: AuthUser | null) => void;
  setLoading: (v: boolean) => void;
  signOut: () => Promise<void>;
  clearLocalAuth: () => void;
  hydrate: () => Promise<void>;
  isCustomer: () => boolean;
  isBarber: () => boolean;
  isAdmin: () => boolean;
};

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  isAuthenticated: false,
  isLoading: true,
  setUser: (user) => set({ user, isAuthenticated: !!user, isLoading: false }),
  setLoading: (v) => set({ isLoading: v }),
  signOut: async () => {
    try { await AuthApi.logout(); } catch (err) {
      const { ErrorReporter } = await import('@/lib/error-reporter');
      ErrorReporter.capture(err, { tag: 'auth.logout' });
    }
    await TokenStorage.clear();
    set({ user: null, isAuthenticated: false });
  },
  clearLocalAuth: () => {
    // Called by API interceptor when refresh fails. Tokens are already wiped from
    // secure storage at that point — we just flip Zustand state so AuthGate redirects
    // to login. No backend call (the session is already invalid).
    set({ user: null, isAuthenticated: false });
  },
  hydrate: async () => {
    const token = await TokenStorage.getAccessToken();
    if (!token) {
      set({ isAuthenticated: false, user: null, isLoading: false });
      return;
    }
    try {
      const profile = await AuthApi.getMyProfile();
      set({
        user: {
          id: profile.id,
          phone: profile.phone,
          displayName: profile.displayName,
          email: profile.email,
          role: profile.role,
          requiresTosConsent: profile.requiresTosConsent,
        },
        isAuthenticated: true,
        isLoading: false,
      });
    } catch (err) {
      // token expired/invalid — beklenen path (401/403) sessizce geçer;
      // network/parse hatasında reporter'a düşer.
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status !== 401 && status !== 403) {
        const { ErrorReporter } = await import('@/lib/error-reporter');
        ErrorReporter.capture(err, { tag: 'auth.hydrate', extra: { status } });
      }
      await TokenStorage.clear();
      set({ isAuthenticated: false, user: null, isLoading: false });
    }
  },
  isCustomer: () => (get().user?.role ?? 'Customer') === 'Customer',
  isBarber: () => {
    const role = get().user?.role;
    return role === 'BarberOwner' || role === 'Stylist';
  },
  isAdmin: () => get().user?.role === 'Admin',
}));
