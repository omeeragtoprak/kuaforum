import { apiClient } from './client';

export type BarberShop = {
  id: string;
  ownerId: string;
  name: string;
  slug: string;
  description?: string;
  phone?: string;
  genderType: number;
  status: number;
  cityId?: string;
  districtId?: string;
  neighborhoodId?: string;
  addressLine1?: string;
  latitude?: number;
  longitude?: number;
  commissionTier?: string;
};

export type Stylist = {
  id: string;
  barberShopId: string;
  displayName: string;
  photoUrl?: string;
  bio?: string;
  isActive: boolean;
  walkInAvailable: boolean;
};

export type Service = {
  id: string;
  name: string;
  description?: string;
  gender: number;
  durationMinutes: number;
  bufferMinutes: number;
  priceMinor: number;
  currency: string;
  isActive: boolean;
};

export type Slot = { startsAt: string; endsAt: string };

export type BarberSearchResult = {
  id: string;
  name: string;
  slug: string;
  description?: string;
  genderType: number;
  addressLine1?: string;
  latitude?: number;
  longitude?: number;
  stylistCount: number;
  serviceCount: number;
  minPriceMinor?: number;
  hasWalkInAvailable: boolean;
  ratingAverage?: number | null;
  ratingCount: number;
};

export type SearchBarbersResponse = {
  items: BarberSearchResult[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type SearchBarbersParams = {
  cityId?: string;
  districtId?: string;
  /** Backend accepts enum names (Unisex/FemaleOnly/MaleOnly) or int (0/1/2) thanks to JsonStringEnumConverter. */
  genderType?: 'Unisex' | 'FemaleOnly' | 'MaleOnly' | number;
  walkInOnly?: boolean;
  minRating?: number;
  /** TL minor units (kuruş). 1 TL = 100. */
  maxPriceMinor?: number;
  page?: number;
  pageSize?: number;
};

export type DayOfWeekKey = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';

export type WorkingHours = {
  dayOfWeek: DayOfWeekKey;
  openTime: string;
  closeTime: string;
  isClosed: boolean;
};

export type TimeOff = {
  id: string;
  startsAt: string;
  endsAt: string;
  reason?: string;
};

export type KycDocumentType = 'TaxLicense' | 'SanitaryPermit' | 'MykCertificate' | 'IdentityCard' | 'Other';

export type KycDocumentRequest = {
  documentType: KycDocumentType;
  fileName: string;
  storageKey: string;
  sizeBytes: number;
  contentType?: string;
};

export type UploadKycResponse = {
  storageKey: string;
  sizeBytes: number;
  contentType: string;
};

/** KycStatus enum: 0=Pending, 1=UnderReview, 2=Approved, 3=Rejected */
export type MyKycStatus = {
  submissionId: string | null;
  status: number | string | null;
  rejectionReason: string | null;
  submittedAt: string | null;
  reviewedAt: string | null;
  documentCount: number;
};

export const BarberApi = {
  async search(params: SearchBarbersParams): Promise<SearchBarbersResponse> {
    const res = await apiClient.get<SearchBarbersResponse>('/v1/barber-shops/search', { params });
    return res.data;
  },
  async submitKyc(shopId: string, documents: KycDocumentRequest[]): Promise<{ id: string }> {
    const res = await apiClient.post(`/v1/owner/barber-shop/${shopId}/kyc/submit`, { documents });
    return res.data;
  },
  async getMyKycStatus(): Promise<MyKycStatus> {
    const res = await apiClient.get<MyKycStatus>('/v1/owner/barber-shop/kyc/status');
    return res.data;
  },
  async uploadKycDocument(args: { uri: string; name: string; contentType: string }): Promise<UploadKycResponse> {
    const form = new FormData();
    form.append('file', {
      uri: args.uri,
      name: args.name,
      type: args.contentType,
    } as unknown as Blob);
    const res = await apiClient.post<UploadKycResponse>('/v1/uploads/kyc', form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
    return res.data;
  },
  async getById(id: string): Promise<BarberShop> {
    const res = await apiClient.get<BarberShop>(`/v1/barber-shops/${id}`);
    return res.data;
  },
  async getStylists(shopId: string): Promise<Stylist[]> {
    const res = await apiClient.get<Stylist[]>(`/v1/barber-shops/${shopId}/stylists`);
    return res.data;
  },
  async addStylist(shopId: string, displayName: string): Promise<{ id: string }> {
    const res = await apiClient.post(`/v1/owner/barber-shop/${shopId}/stylists`, { displayName });
    return res.data;
  },
  async updateStylist(stylistId: string, body: { displayName?: string; photoUrl?: string; bio?: string; isActive?: boolean }): Promise<void> {
    await apiClient.patch(`/v1/stylists/${stylistId}`, body);
  },
  async toggleStylistWalkIn(stylistId: string, available: boolean): Promise<void> {
    await apiClient.post(`/v1/stylists/${stylistId}/walk-in`, { available });
  },
  async getServices(shopId: string): Promise<Service[]> {
    const res = await apiClient.get<Service[]>(`/v1/barber-shops/${shopId}/services`);
    return res.data;
  },
  async computeAvailability(stylistId: string, fromDate: string, toDate: string, serviceCatalogIds: string[]): Promise<Slot[]> {
    const res = await apiClient.post<Slot[]>('/v1/availability/compute', {
      stylistId,
      fromDate,
      toDate,
      serviceCatalogIds,
    });
    return res.data;
  },

  // Owner self-service
  async getMyShop(): Promise<BarberShop | null> {
    try {
      const res = await apiClient.get<BarberShop>('/v1/owner/barber-shop');
      return res.data;
    } catch (err) {
      return null;
    }
  },
  async createService(shopId: string, body: {
    name: string;
    gender: number;
    durationMinutes: number;
    priceMinor: number;
    bufferMinutes: number;
    description?: string;
  }): Promise<{ id: string }> {
    const res = await apiClient.post<{ id: string }>(`/v1/owner/barber-shop/${shopId}/services`, body);
    return res.data;
  },
  async updateService(shopId: string, serviceId: string, body: {
    name?: string;
    description?: string | null;
    durationMinutes?: number;
    priceMinor?: number;
    bufferMinutes?: number;
  }): Promise<void> {
    await apiClient.put(`/v1/owner/barber-shop/${shopId}/services/${serviceId}`, body);
  },
  async deleteService(shopId: string, serviceId: string): Promise<void> {
    await apiClient.delete(`/v1/owner/barber-shop/${shopId}/services/${serviceId}`);
  },
  async toggleStylistWalkIn(stylistId: string, available: boolean): Promise<void> {
    await apiClient.post(`/v1/stylists/${stylistId}/walk-in`, { available });
  },

  async getWorkingHours(stylistId: string): Promise<WorkingHours[]> {
    const res = await apiClient.get<WorkingHours[]>(`/v1/stylists/${stylistId}/working-hours`);
    return res.data;
  },
  async setWorkingHours(stylistId: string, body: { dayOfWeek: number; openTime: string; closeTime: string; isClosed: boolean }): Promise<void> {
    await apiClient.put(`/v1/stylists/${stylistId}/working-hours`, body);
  },
  async getTimeOffs(stylistId: string): Promise<TimeOff[]> {
    const res = await apiClient.get<TimeOff[]>(`/v1/stylists/${stylistId}/time-offs`);
    return res.data;
  },
  async addTimeOff(stylistId: string, body: { startsAt: string; endsAt: string; reason?: string }): Promise<{ id: string }> {
    const res = await apiClient.post<{ id: string }>(`/v1/stylists/${stylistId}/time-off`, body);
    return res.data;
  },
  async deleteTimeOff(stylistId: string, timeOffId: string): Promise<void> {
    await apiClient.delete(`/v1/stylists/${stylistId}/time-offs/${timeOffId}`);
  },
};
