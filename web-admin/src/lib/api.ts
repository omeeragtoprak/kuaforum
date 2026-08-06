'use client';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5100';
const TOKEN_KEY = 'kuaforum.admin.access_token';
const REFRESH_TOKEN_KEY = 'kuaforum.admin.refresh_token';
const FINGERPRINT_KEY = 'kuaforum.admin.device_fingerprint';

function randomFingerprint(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return `web-admin-${crypto.randomUUID()}`;
  }
  return `web-admin-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

export const adminAuth = {
  getToken(): string | null {
    if (typeof window === 'undefined') return null;
    return localStorage.getItem(TOKEN_KEY);
  },
  setToken(token: string) {
    if (typeof window === 'undefined') return;
    localStorage.setItem(TOKEN_KEY, token);
  },
  getRefreshToken(): string | null {
    if (typeof window === 'undefined') return null;
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  },
  setRefreshToken(token: string) {
    if (typeof window === 'undefined') return;
    localStorage.setItem(REFRESH_TOKEN_KEY, token);
  },
  // Backend RefreshTokenCommand rejects a refresh call whose DeviceFingerprint doesn't
  // match the one stored on the RefreshToken at issue time (device-bound rotation).
  // AdminLoginRequest.DeviceFingerprint falls back server-side to the per-connection
  // Connection.Id when omitted, which is NOT stable across requests — so the client
  // must generate and persist its own fingerprint and send it both at login and at
  // every refresh call, or refresh will 401 with "Cihaz parmak izi eşleşmiyor.".
  getDeviceFingerprint(): string {
    if (typeof window === 'undefined') return 'web-admin-ssr';
    let fp = localStorage.getItem(FINGERPRINT_KEY);
    if (!fp) {
      fp = randomFingerprint();
      localStorage.setItem(FINGERPRINT_KEY, fp);
    }
    return fp;
  },
  setSession(tokens: { accessToken: string; refreshToken: string }) {
    this.setToken(tokens.accessToken);
    this.setRefreshToken(tokens.refreshToken);
  },
  clear() {
    if (typeof window === 'undefined') return;
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    // Device fingerprint is intentionally kept — it identifies this browser/device,
    // not the session, and must stay stable across logout/login cycles.
  },
};

export type ApiError = { detail?: string; title?: string; status?: number; errors?: Record<string, string[]> };

export class ApiClientError extends Error {
  constructor(public status: number, public payload: ApiError | null) {
    super(payload?.detail ?? payload?.title ?? `HTTP ${status}`);
  }
}

// Single-flight guard: if several requests 401 at once, only one /v1/auth/refresh
// call goes out; the rest await the same promise.
let refreshInFlight: Promise<boolean> | null = null;

async function refreshAccessToken(): Promise<boolean> {
  const refreshToken = adminAuth.getRefreshToken();
  if (!refreshToken) return false;
  const deviceFingerprint = adminAuth.getDeviceFingerprint();

  try {
    // Raw fetch — deliberately bypasses apiFetch to avoid recursing into the
    // refresh-on-401 logic below.
    const res = await fetch(`${API_BASE_URL}/v1/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken, deviceFingerprint }),
    });
    if (!res.ok) return false;
    const data = (await res.json()) as { accessToken?: string; refreshToken?: string };
    if (!data.accessToken || !data.refreshToken) return false;
    adminAuth.setSession({ accessToken: data.accessToken, refreshToken: data.refreshToken });
    return true;
  } catch {
    return false;
  }
}

function redirectToLogin() {
  if (typeof window === 'undefined') return;
  if (window.location.pathname === '/login') return;
  window.location.href = '/login';
}

export async function apiFetch<T = unknown>(
  path: string,
  init: RequestInit = {},
  _retried = false,
): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('Content-Type', 'application/json');
  const token = adminAuth.getToken();
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const res = await fetch(`${API_BASE_URL}${path}`, { ...init, headers });

  const isAuthPath = path.startsWith('/v1/auth/');

  if (res.status === 401 && !isAuthPath && !_retried) {
    if (!refreshInFlight) refreshInFlight = refreshAccessToken();
    const refreshed = await refreshInFlight;
    refreshInFlight = null;

    if (refreshed) {
      return apiFetch<T>(path, init, true);
    }

    adminAuth.clear();
    redirectToLogin();
    throw new ApiClientError(401, { detail: 'Oturum süresi doldu, tekrar giriş yapın.' });
  }

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

// Binary counterpart of apiFetch — used for KYC document content, which is streamed
// (not JSON) from an authenticated proxy endpoint. <img src> can't carry an
// Authorization header, so the caller fetches the bytes here and builds an
// object URL from the returned Blob. Mirrors apiFetch's single-retry-on-401
// refresh flow so an expired access token doesn't surface as a raw 401.
export async function apiFetchBlob(
  path: string,
  _retried = false,
): Promise<{ blob: Blob; contentType: string }> {
  const token = adminAuth.getToken();
  const headers = new Headers();
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const res = await fetch(`${API_BASE_URL}${path}`, { headers });

  if (res.status === 401 && !_retried) {
    if (!refreshInFlight) refreshInFlight = refreshAccessToken();
    const refreshed = await refreshInFlight;
    refreshInFlight = null;

    if (refreshed) {
      return apiFetchBlob(path, true);
    }

    adminAuth.clear();
    redirectToLogin();
    throw new ApiClientError(401, { detail: 'Oturum süresi doldu, tekrar giriş yapın.' });
  }

  if (!res.ok) {
    let payload: ApiError | null = null;
    try {
      payload = await res.json();
    } catch {
      // ignore — content endpoint errors are usually ProblemDetails but not always
    }
    throw new ApiClientError(res.status, payload);
  }

  const blob = await res.blob();
  const contentType = res.headers.get('Content-Type') ?? 'application/octet-stream';
  return { blob, contentType };
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
  // Backend enum serialization is JsonStringEnumConverter — this arrives as
  // e.g. "Pending", not 0. Kept as string|number and read via String(status)
  // everywhere (see bookings/barber-shops pages for the same pattern).
  status: number | string;
};

export type KycQueuePage = {
  items: KycQueueItem[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type KycDocumentItem = {
  id: string;
  documentType: number | string;
  fileName: string;
  contentType?: string | null;
  sizeBytes: number;
  uploadedAt: string;
};

export type PendingReview = {
  id: string;
  customerId: string;
  customerName?: string | null;
  barberShopId: string;
  barberShopName?: string | null;
  stylistId: string;
  stylistName?: string | null;
  rating: number;
  body?: string;
  createdAt: string;
};

export type AdminUser = {
  id: string;
  phoneE164?: string;
  email?: string;
  displayName?: string;
  // Backend JsonStringEnumConverter kullanıyor — canlıda enum adı ("Active") gelir; sayı da desteklenir.
  role: number | string;
  status: number | string;
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

// ---------------------------------------------------------------------------
// RFC-012 faturalama / tahsilat
// ---------------------------------------------------------------------------

/** Backend CommissionStatementStatus (Open=0 … SuspensionApplied=4). */
export type CommissionStatementStatusName =
  | 'Open'
  | 'Notified'
  | 'Paid'
  | 'Overdue'
  | 'SuspensionApplied';

export type PlatformSettingsDto = {
  id: string;
  /** 0-1 arası decimal (backend doğrulaması: >0 ve <=0.5). UI yüzde gösterir. */
  commissionRateGlobal: number;
  commissionPaymentIban?: string | null;
  commissionPaymentBankName?: string | null;
  commissionPaymentAccountHolderName?: string | null;
  /**
   * RFC-013 C3 alanları (kapora oranı alt/üst sınırı, 0-1 decimal). Bu ekranın konusu değil
   * ama PUT tüm satırı yazdığı için okunup geri gönderilmeleri gerekir — aksi halde 0'a
   * düşer ve backend validasyonu (GreaterThan(0)) 400 döner.
   */
  depositPercentMin?: number;
  depositPercentMax?: number;
};

export type UpdatePlatformSettingsBody = {
  commissionRateGlobal: number;
  commissionPaymentIban: string | null;
  commissionPaymentBankName: string | null;
  commissionPaymentAccountHolderName: string | null;
  // Bkz. PlatformSettingsDto: GET'ten okunan değerler aynen geri yazılır (kaybolmasın diye).
  depositPercentMin?: number;
  depositPercentMax?: number;
};

export type AdminCommissionStatementDto = {
  id: string;
  barberShopId: string;
  barberShopName: string;
  /** DateOnly — "2026-07-01" formatında gelir. */
  periodStart: string;
  periodEnd: string;
  amountDueMinor: number;
  dueDate: string;
  // JsonStringEnumConverter → "Notified"; sayı da tolere edilir (String(status) ile okunur).
  status: number | string;
  isDisputed: boolean;
  // Admin kuyruk DTO'su (AdminCommissionStatementItemDto) şu an matrah/oran taşımıyor.
  // Backend eklediğinde tablo otomatik doldurur — o güne kadar "—" gösterilir.
  basisAmountMinor?: number;
  rateApplied?: number;
};

export type AdminCommissionStatementsPage = {
  items: AdminCommissionStatementDto[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type AdminStatementsQuery = {
  status?: CommissionStatementStatusName;
  disputedOnly?: boolean;
  page?: number;
  pageSize?: number;
};

export const adminApi = {
  async login(email: string, password: string, totpCode: string) {
    // deviceFingerprint must be stable across requests — it's persisted client-side and
    // reused on every /v1/auth/refresh call (see adminAuth.getDeviceFingerprint doc comment).
    const deviceFingerprint = adminAuth.getDeviceFingerprint();
    return apiFetch<{ accessToken: string; refreshToken: string; user: { id: string; role: string } }>(
      '/v1/auth/admin/login',
      {
        method: 'POST',
        body: JSON.stringify({ email, password, totpCode, deviceFingerprint, deviceName: 'Web Admin' }),
      },
    );
  },
  async dashboard(): Promise<DashboardStats> {
    return apiFetch<DashboardStats>('/v1/admin/dashboard');
  },
  async kycQueue(params: { status?: number; page?: number; pageSize?: number } = {}): Promise<KycQueuePage> {
    const qs = new URLSearchParams();
    if (params.status !== undefined) qs.set('status', String(params.status));
    qs.set('page', String(params.page ?? 1));
    qs.set('pageSize', String(params.pageSize ?? 20));
    return apiFetch<KycQueuePage>(`/v1/admin/kyc/queue?${qs}`);
  },
  async reviewKyc(submissionId: string, approve: boolean, rejectionReason?: string, subMerchantKey?: string) {
    return apiFetch(`/v1/admin/kyc/${submissionId}/review`, {
      method: 'POST',
      body: JSON.stringify({ approve, rejectionReason, subMerchantKey }),
    });
  },
  async kycDocuments(submissionId: string): Promise<KycDocumentItem[]> {
    return apiFetch<KycDocumentItem[]>(`/v1/admin/kyc/${submissionId}/documents`);
  },
  async kycDocumentContent(documentId: string): Promise<{ blob: Blob; contentType: string }> {
    return apiFetchBlob(`/v1/admin/kyc/documents/${documentId}/content`);
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
  async listAuditLogs(params: AuditLogQuery = {}): Promise<AuditLogPage> {
    const qs = new URLSearchParams();
    if (params.actorUserId) qs.set('actorUserId', params.actorUserId);
    if (params.actionType) qs.set('actionType', params.actionType);
    if (params.resourceType) qs.set('resourceType', params.resourceType);
    if (params.resourceId) qs.set('resourceId', params.resourceId);
    if (params.fromUtc) qs.set('fromUtc', params.fromUtc);
    if (params.toUtc) qs.set('toUtc', params.toUtc);
    qs.set('page', String(params.page ?? 1));
    qs.set('pageSize', String(params.pageSize ?? 50));
    return apiFetch<AuditLogPage>(`/v1/admin/audit-logs?${qs}`);
  },

  // --- RFC-012 faturalama -------------------------------------------------
  async platformSettings(): Promise<PlatformSettingsDto> {
    return apiFetch<PlatformSettingsDto>('/v1/admin/billing/settings');
  },
  /**
   * PUT tüm alanları birlikte yazar (banka bilgisi kısmi güncellenmez) — çağıran taraf
   * yüklediği mevcut değerleri geri göndermeli. commissionRateGlobal 0-1 arası decimal.
   */
  async updatePlatformSettings(body: UpdatePlatformSettingsBody): Promise<void> {
    await apiFetch<void>('/v1/admin/billing/settings', {
      method: 'PUT',
      body: JSON.stringify(body),
    });
  },
  /** rateOverride: 0-1 arası decimal, null → override kaldırılır (global orana döner). */
  async setBarberShopCommissionRate(shopId: string, rateOverride: number | null): Promise<void> {
    await apiFetch<void>(`/v1/admin/billing/barber-shops/${shopId}/commission-rate`, {
      method: 'PUT',
      body: JSON.stringify({ rateOverride }),
    });
  },
  async listCommissionStatements(params: AdminStatementsQuery = {}): Promise<AdminCommissionStatementsPage> {
    const qs = new URLSearchParams();
    // Enum query binding Enum.TryParse üzerinden — isim birebir doğru yazılmalı ("Notified").
    if (params.status) qs.set('status', params.status);
    if (params.disputedOnly) qs.set('disputedOnly', 'true');
    qs.set('page', String(params.page ?? 1));
    qs.set('pageSize', String(params.pageSize ?? 25));
    return apiFetch<AdminCommissionStatementsPage>(`/v1/admin/billing/statements?${qs}`);
  },
  async markStatementPaid(statementId: string, reason: string): Promise<void> {
    await apiFetch<void>(`/v1/admin/billing/statements/${statementId}/mark-paid`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    });
  },
  async adjustStatementAmount(statementId: string, newAmountDueMinor: number, reason: string): Promise<void> {
    await apiFetch<void>(`/v1/admin/billing/statements/${statementId}/adjust`, {
      method: 'POST',
      body: JSON.stringify({ newAmountDueMinor, reason }),
    });
  },
  /** newDueDate: DateOnly — "2026-09-15" (yyyy-MM-dd). */
  async extendStatementDueDate(statementId: string, newDueDate: string, reason: string): Promise<void> {
    await apiFetch<void>(`/v1/admin/billing/statements/${statementId}/extend-due`, {
      method: 'POST',
      body: JSON.stringify({ newDueDate, reason }),
    });
  },
};

export type AuditLogQuery = {
  actorUserId?: string;
  actionType?: string;
  resourceType?: string;
  resourceId?: string;
  fromUtc?: string;
  toUtc?: string;
  page?: number;
  pageSize?: number;
};

export type AuditLogItem = {
  id: number;
  actorUserId: string | null;
  actorDisplayName: string | null;
  actorType: string;
  actionType: string;
  resourceType: string;
  resourceId: string;
  payloadJson: string | null;
  ip: string | null;
  requestId: string | null;
  createdAt: string;
};

export type AuditLogPage = {
  items: AuditLogItem[];
  page: number;
  pageSize: number;
  totalCount: number;
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

/**
 * Backend DateOnly ("2026-07-31") — new Date(str) UTC gece yarısı olarak parse edip
 * negatif offset'li makinede bir gün geriye kaydırdığı için parçalayarak lokal tarih kurar.
 */
export function formatDateOnly(value: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!m) return value;
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return new Intl.DateTimeFormat('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' }).format(d);
}

/** 0-1 arası decimal → yüzde metni ("0.185" → "18,5"). Float gürültüsü yuvarlanır. */
export function rateToPercentText(rate: number): string {
  const pct = Math.round(rate * 10000) / 100;
  return String(pct).replace('.', ',');
}

/** Yüzde girdisi → 0-1 arası decimal. Geçersizse null ("18,5" → 0.185). */
export function percentTextToRate(input: string): number | null {
  const normalized = input.trim().replace(',', '.');
  if (normalized === '') return null;
  const pct = Number(normalized);
  if (!Number.isFinite(pct)) return null;
  return Math.round(pct * 100) / 10000;
}

/**
 * TL girdisi → kuruş. Virgül varsa ondalık ayırıcı virgüldür (nokta binlik: "1.234,56"),
 * yoksa nokta ondalık kabul edilir ("1234.56"). Geçersizse null.
 */
export function parseTryToMinor(input: string): number | null {
  const raw = input.trim().replace(/\s|₺/g, '');
  if (raw === '') return null;
  const normalized = raw.includes(',')
    ? raw.replace(/\./g, '').replace(',', '.')
    : raw;
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) return null;
  return Math.round(Number(normalized) * 100);
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
