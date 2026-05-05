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
};

type AuthState = {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  setUser: (user: AuthUser | null) => void;
  setLoading: (v: boolean) => void;
  signOut: () => Promise<void>;
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
    try { await AuthApi.logout(); } catch { /* ignore */ }
    await TokenStorage.clear();
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
        },
        isAuthenticated: true,
        isLoading: false,
      });
    } catch {
      // token expired/invalid
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
