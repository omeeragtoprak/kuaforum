import { create } from 'zustand';
import { TokenStorage } from '@/lib/secure-storage';

export type AuthUser = {
  id: string;
  phone?: string | null;
  displayName?: string | null;
  role: 'Customer' | 'BarberOwner' | 'Stylist' | 'Admin';
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
};

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  isAuthenticated: false,
  isLoading: true,
  setUser: (user) => set({ user, isAuthenticated: !!user, isLoading: false }),
  setLoading: (v) => set({ isLoading: v }),
  signOut: async () => {
    await TokenStorage.clear();
    set({ user: null, isAuthenticated: false });
  },
  hydrate: async () => {
    const token = await TokenStorage.getAccessToken();
    set({ isAuthenticated: !!token, isLoading: false });
  },
  isCustomer: () => (get().user?.role ?? 'Customer') === 'Customer',
  isBarber: () => {
    const role = get().user?.role;
    return role === 'BarberOwner' || role === 'Stylist';
  },
}));
