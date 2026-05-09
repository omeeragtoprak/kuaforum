import { create } from 'zustand';

/**
 * Keşfet sayfası kullanıcının seçtiği şehri tutar. Default null —
 * cities query yüklenince Erzurum (plate 25) otomatik seçilir
 * (test demo customer için). Kullanıcı "Bölgemi değiştir" ile başka
 * şehir seçerse store güncellenir.
 *
 * Persistence yok (uygulama açıldıkça default Erzurum tekrar seçilir).
 * Production: cihaz konumundan geocoding ile otomatik tespit edilebilir.
 */
type DiscoveryState = {
  cityId: string | null;
  cityName: string | null;
  setCity: (cityId: string, cityName: string) => void;
  clear: () => void;
};

export const useDiscoveryStore = create<DiscoveryState>((set) => ({
  cityId: null,
  cityName: null,
  setCity: (cityId, cityName) => set({ cityId, cityName }),
  clear: () => set({ cityId: null, cityName: null }),
}));

/** Demo / development default — test customer Erzurum'da. Plate 25. */
export const DEFAULT_CITY_PLATE = 25;
export const DEFAULT_CITY_NAME = 'Erzurum';
