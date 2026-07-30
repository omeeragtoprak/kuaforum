import { create } from 'zustand';

/** Backend `SalonGenderType` enum'ı ile senkron string adları (JsonStringEnumConverter). */
export type OnboardingGenderType = 'Unisex' | 'FemaleOnly' | 'MaleOnly';

// NOT (O5 review M-1): cityId/districtId/neighborhoodId store'dan kaldırıldı —
// hiçbir yerde OKUNMUYORDU; il/ilçe/mahalle TAM nesneleri adres ekranının local
// state'inde yaşıyor (isim + plaka kodu gerekli, salt ID yetmiyor).
type OnboardingFields = {
  shopName: string;
  genderType: OnboardingGenderType;
  phone: string;
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
