import { apiClient } from './client';

export type City = { id: string; plateCode: number; name: string; slug: string };
export type District = { id: string; cityId: string; name: string; slug: string };
export type Neighborhood = { id: string; districtId: string; name: string; slug: string; postalCode?: string };

export const GeographyApi = {
  async getCities(): Promise<City[]> {
    const res = await apiClient.get<City[]>('/v1/geography/cities');
    return res.data;
  },
  async getDistricts(cityId: string): Promise<District[]> {
    const res = await apiClient.get<District[]>(`/v1/geography/cities/${cityId}/districts`);
    return res.data;
  },
  async getNeighborhoods(districtId: string): Promise<Neighborhood[]> {
    const res = await apiClient.get<Neighborhood[]>(`/v1/geography/districts/${districtId}/neighborhoods`);
    return res.data;
  },
};
