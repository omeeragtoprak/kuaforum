/**
 * RFC-007 owner onboarding — adres adımında backend `SetBarberShopAddressCommand`
 * `Latitude`/`Longitude` alanlarını ZORUNLU kılıyor (Türkiye sınırı doğrulaması:
 * lat 35.5-42.5, lng 25.5-45.0) ama mobilde henüz gerçek geocoding yok
 * (bkz. discovery-store.ts yorumu). Yeni native modül (expo-location) eklemek
 * yerine — "Bu native modül gerek mi?" push-back — il plaka koduna göre YAKLAŞIK
 * il merkezi koordinatı kullanıyoruz. PostGIS arama şu an bu alanları sıralama/mesafe
 * için KULLANMIYOR (SearchBarbersQuery sadece CreatedAt'e göre sıralıyor), yani MVP
 * için hassasiyet kritik değil.
 *
 * TODO(V1.5): gerçek adres geocoding (Google Geocoding API veya expo-location ile
 * "mevcut konumumu kullan") — bkz. RFC-007 kapanış notları.
 */
export type CityCentroid = { latitude: number; longitude: number };

/** Plaka kodu → yaklaşık il merkezi koordinatı. En kalabalık/erken lansman illeri kapsanıyor. */
const CITY_CENTROIDS_BY_PLATE: Record<number, CityCentroid> = {
  1: { latitude: 37.0, longitude: 35.32 }, // Adana
  6: { latitude: 39.93, longitude: 32.86 }, // Ankara
  7: { latitude: 36.9, longitude: 30.71 }, // Antalya
  9: { latitude: 37.85, longitude: 27.85 }, // Aydın
  10: { latitude: 39.65, longitude: 27.89 }, // Balıkesir
  16: { latitude: 40.18, longitude: 29.06 }, // Bursa
  17: { latitude: 40.15, longitude: 26.41 }, // Çanakkale
  20: { latitude: 37.77, longitude: 29.09 }, // Denizli
  21: { latitude: 37.91, longitude: 40.24 }, // Diyarbakır
  22: { latitude: 41.68, longitude: 26.56 }, // Edirne
  23: { latitude: 38.68, longitude: 39.22 }, // Elazığ
  25: { latitude: 39.9, longitude: 41.27 }, // Erzurum (demo default şehir)
  26: { latitude: 39.78, longitude: 30.52 }, // Eskişehir
  27: { latitude: 37.06, longitude: 37.38 }, // Gaziantep
  31: { latitude: 36.2, longitude: 36.16 }, // Hatay
  33: { latitude: 36.81, longitude: 34.64 }, // Mersin
  34: { latitude: 41.01, longitude: 28.98 }, // İstanbul
  35: { latitude: 38.42, longitude: 27.14 }, // İzmir
  38: { latitude: 38.73, longitude: 35.49 }, // Kayseri
  41: { latitude: 40.85, longitude: 29.88 }, // Kocaeli
  42: { latitude: 37.87, longitude: 32.48 }, // Konya
  43: { latitude: 39.42, longitude: 29.98 }, // Kütahya
  44: { latitude: 38.35, longitude: 38.31 }, // Malatya
  45: { latitude: 38.61, longitude: 27.43 }, // Manisa
  46: { latitude: 37.57, longitude: 36.92 }, // Kahramanmaraş
  48: { latitude: 37.22, longitude: 28.36 }, // Muğla
  52: { latitude: 40.98, longitude: 37.88 }, // Ordu
  53: { latitude: 41.02, longitude: 40.52 }, // Rize
  54: { latitude: 40.78, longitude: 30.4 }, // Sakarya
  55: { latitude: 41.29, longitude: 36.33 }, // Samsun
  58: { latitude: 39.75, longitude: 37.02 }, // Sivas
  59: { latitude: 40.98, longitude: 27.51 }, // Tekirdağ
  61: { latitude: 41.0, longitude: 39.72 }, // Trabzon
  63: { latitude: 37.16, longitude: 38.79 }, // Şanlıurfa
  65: { latitude: 38.49, longitude: 43.38 }, // Van
};

/** Kapsanmayan iller için Türkiye'nin coğrafi ortalamasına yakın güvenli varsayılan (Sivas civarı). */
const FALLBACK_CENTROID: CityCentroid = { latitude: 39.0, longitude: 35.0 };

export function getCityCentroid(plateCode: number): CityCentroid {
  return CITY_CENTROIDS_BY_PLATE[plateCode] ?? FALLBACK_CENTROID;
}
