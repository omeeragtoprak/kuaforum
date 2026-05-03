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
  genderType?: number;
  walkInOnly?: boolean;
  page?: number;
  pageSize?: number;
};

export const BarberApi = {
  async search(params: SearchBarbersParams): Promise<SearchBarbersResponse> {
    const res = await apiClient.get<SearchBarbersResponse>('/v1/barber-shops/search', { params });
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
};
