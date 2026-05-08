'use client';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5100';
const TOKEN_KEY = 'kuaforum.admin.access_token';

export const adminAuth = {
  getToken(): string | null {
    if (typeof window === 'undefined') return null;
    return localStorage.getItem(TOKEN_KEY);
  },
  setToken(token: string) {
    if (typeof window === 'undefined') return;
    localStorage.setItem(TOKEN_KEY, token);
  },
  clear() {
    if (typeof window === 'undefined') return;
    localStorage.removeItem(TOKEN_KEY);
  },
};

export type ApiError = { detail?: string; title?: string; status?: number; errors?: Record<string, string[]> };

export class ApiClientError extends Error {
  constructor(public status: number, public payload: ApiError | null) {
    super(payload?.detail ?? payload?.title ?? `HTTP ${status}`);
  }
}

export async function apiFetch<T = unknown>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('Content-Type', 'application/json');
  const token = adminAuth.getToken();
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const res = await fetch(`${API_BASE_URL}${path}`, { ...init, headers });

  if (!res.ok) {
    let payload: ApiError | null = null;
    try {
      payload = await res.json();
    } catch {
      // ignore
    }
    throw new ApiClientError(res.status, payload);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export type DashboardStats = {
  totalUsers: number;
  customerCount: number;
  barberOwnerCount: number;
  activeBarberShops: number;
  pendingKycCount: number;
  pendingReviewsCount: number;
  todaysBookings: number;
  todaysGmvMinor: number;
};

export type KycQueueItem = {
  submissionId: string;
  barberShopId: string;
  barberShopName: string;
  submittedByUserId: string;
  submitterName?: string;
  submittedAt: string;
  documentCount: number;
  status: number;
};

export type PendingReview = {
  id: string;
  customerId: string;
  stylistId: string;
  rating: number;
  body?: string;
  createdAt: string;
};

export type AdminUser = {
  id: string;
  phoneE164?: string;
  email?: string;
  displayName?: string;
  role: number;
  status: number;
  createdAt: string;
  lastLoginAt?: string;
  deletedAt?: string;
};

export type UsersAdminPage = {
  items: AdminUser[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export const adminApi = {
  async login(email: string, password: string, totpCode: string) {
    return apiFetch<{ accessToken: string; refreshToken: string; user: { id: string; role: string } }>(
      '/v1/auth/admin/login',
      { method: 'POST', body: JSON.stringify({ email, password, totpCode }) },
    );
  },
  async dashboard(): Promise<DashboardStats> {
    return apiFetch<DashboardStats>('/v1/admin/dashboard');
  },
  async kycQueue(status?: number): Promise<KycQueueItem[]> {
    const qs = status !== undefined ? `?status=${status}` : '';
    return apiFetch<KycQueueItem[]>(`/v1/admin/kyc/queue${qs}`);
  },
  async reviewKyc(submissionId: string, approve: boolean, rejectionReason?: string, subMerchantKey?: string) {
    return apiFetch(`/v1/admin/kyc/${submissionId}/review`, {
      method: 'POST',
      body: JSON.stringify({ approve, rejectionReason, subMerchantKey }),
    });
  },
  async pendingReviews(): Promise<PendingReview[]> {
    return apiFetch<PendingReview[]>('/v1/admin/reviews/pending');
  },
  async approveReview(id: string) {
    return apiFetch(`/v1/admin/reviews/${id}/approve`, { method: 'POST' });
  },
  async rejectReview(id: string, reason: number) {
    return apiFetch(`/v1/admin/reviews/${id}/reject`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    });
  },
  async listUsers(params: { search?: string; role?: number; status?: number; page?: number; pageSize?: number } = {}): Promise<UsersAdminPage> {
    const qs = new URLSearchParams();
    if (params.search) qs.set('search', params.search);
    if (params.role !== undefined) qs.set('role', String(params.role));
    if (params.status !== undefined) qs.set('status', String(params.status));
    qs.set('page', String(params.page ?? 1));
    qs.set('pageSize', String(params.pageSize ?? 20));
    return apiFetch<UsersAdminPage>(`/v1/admin/users?${qs}`);
  },
  async suspendUser(userId: string, reason: string) {
    return apiFetch(`/v1/admin/users/${userId}/suspend`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    });
  },
  async unsuspendUser(userId: string) {
    return apiFetch(`/v1/admin/users/${userId}/unsuspend`, { method: 'POST' });
  },
  async listBookings(params: AdminBookingsQuery = {}): Promise<AdminBookingsPage> {
    const qs = new URLSearchParams();
    if (params.from) qs.set('from', params.from);
    if (params.to) qs.set('to', params.to);
    if (params.status !== undefined) qs.set('status', String(params.status));
    if (params.barberShopId) qs.set('barberShopId', params.barberShopId);
    if (params.search) qs.set('search', params.search);
    qs.set('page', String(params.page ?? 1));
    qs.set('pageSize', String(params.pageSize ?? 25));
    return apiFetch<AdminBookingsPage>(`/v1/admin/bookings?${qs}`);
  },
  async cancelBooking(bookingId: string, reason: string): Promise<{ refundPercentage: number }> {
    return apiFetch<{ refundPercentage: number }>(`/v1/admin/bookings/${bookingId}/cancel`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    });
  },
  async listBarberShops(params: AdminShopsQuery = {}): Promise<AdminBarberShopsPage> {
    const qs = new URLSearchParams();
    if (params.search) qs.set('search', params.search);
    if (params.status !== undefined) qs.set('status', String(params.status));
    qs.set('page', String(params.page ?? 1));
    qs.set('pageSize', String(params.pageSize ?? 25));
    return apiFetch<AdminBarberShopsPage>(`/v1/admin/barber-shops?${qs}`);
  },
  async suspendBarberShop(shopId: string, reason: string): Promise<void> {
    await apiFetch<void>(`/v1/admin/barber-shops/${shopId}/suspend`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    });
  },
  async reactivateBarberShop(shopId: string): Promise<void> {
    await apiFetch<void>(`/v1/admin/barber-shops/${shopId}/reactivate`, {
      method: 'POST',
    });
  },
};

export type AdminBookingsQuery = {
  from?: string;
  to?: string;
  status?: number;
  barberShopId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
};

export type AdminBookingDto = {
  id: string;
  customerId: string;
  customerName: string | null;
  customerPhone: string | null;
  barberShopId: string;
  shopName: string;
  stylistId: string;
  stylistName: string;
  scheduledAt: string;
  scheduledEndsAt: string;
  status: number | string;
  totalAmountMinor: number;
  currency: string;
};

export type AdminBookingsPage = {
  items: AdminBookingDto[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type AdminShopsQuery = {
  search?: string;
  status?: number;
  page?: number;
  pageSize?: number;
};

export type AdminBarberShopDto = {
  id: string;
  name: string;
  slug: string;
  ownerDisplayName: string | null;
  ownerPhone: string | null;
  status: number | string;
  genderType: number | string;
  stylistCount: number;
  serviceCount: number;
  completedBookingsCount: number;
  createdAt: string;
};

export type AdminBarberShopsPage = {
  items: AdminBarberShopDto[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export function formatTry(minor: number): string {
  return new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', minimumFractionDigits: 0 }).format(minor / 100);
}

export function formatDateTime(iso: string): string {
  return new Intl.DateTimeFormat('tr-TR', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(iso));
}
