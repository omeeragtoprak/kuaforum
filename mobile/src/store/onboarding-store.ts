import { create } from 'zustand';

/** Backend `SalonGenderType` enum'ı ile senkron string adları (JsonStringEnumConverter). */
export type OnboardingGenderType = 'Unisex' | 'FemaleOnly' | 'MaleOnly';

type OnboardingFields = {
  shopName: string;
  genderType: OnboardingGenderType;
  phone: string;
  cityId: string | null;
  districtId: string | null;
  /**
   * Görev tanımındaki alan listesinde yoktu ama backend `SetAddressRequest`
   * `NeighborhoodId`'yi ZORUNLU kılıyor (il/ilçe yetmiyor) — adres adımının
   * gerçekten submit edilebilmesi için eklendi.
   */
  neighborhoodId: string | null;
  addressLine: string;
  shopId: string | null;
};

type OnboardingState = OnboardingFields & {
  setField: <K extends keyof OnboardingFields>(key: K, value: OnboardingFields[K]) => void;
  setShopId: (shopId: string | null) => void;
  reset: () => void;
};

const initialFields: OnboardingFields = {
  shopName: '',
  genderType: 'Unisex',
  phone: '',
  cityId: null,
  districtId: null,
  neighborhoodId: null,
  addressLine: '',
  shopId: null,
};

/**
 * Owner onboarding sihirbazının oturum-içi form state'i. Persist YOK — `shopId`
 * her adımda backend'e yazıldığı için idempotent devam sunucu kaynaklı
 * (`resolveOnboardingStep`, bkz. lib/onboarding.ts); form alanları yalnızca
 * aynı oturumda geri/ileri gidişte kaybolmasın diye tutuluyor.
 */
export const useOnboardingStore = create<OnboardingState>((set) => ({
  ...initialFields,
  setField: (key, value) => set({ [key]: value } as Partial<OnboardingFields>),
  setShopId: (shopId) => set({ shopId }),
  reset: () => set({ ...initialFields }),
}));
