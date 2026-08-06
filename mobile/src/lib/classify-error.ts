import axios from 'axios';

/**
 * G12 fix: "Bulunamadı" (kayıt silinmiş/yok) ile "sunucuya ulaşılamadı" (geçici ağ/5xx/timeout
 * hatası) aynı ekranda birleşiyordu — kullanıcı "bu kuaför artık yok" sanıp geri dönüyordu,
 * oysa doğru aksiyon tekrar denemekti. Bu tek dosyada yaşayan saf fonksiyon, TanStack Query'nin
 * ürettiği axios hatasının ŞEKLİNE bakarak ayrımı yapar — kopyalanmasın, her ekran buradan alsın.
 *
 * Karar tablosu:
 *  - response.status === 404                       -> 'notFound'  (kayıt gerçekten yok/kaldırılmış)
 *  - response.status varsa ama 404 değil (401/5xx…) -> 'network'   (sunucu cevap verdi ama hata; retry mantıklı)
 *  - response yok (ERR_NETWORK, DNS, connection refused) -> 'network'
 *  - timeout (ECONNABORTED / ETIMEDOUT)             -> 'network'
 *  - axios hatası değil / tanınmayan şekil           -> 'network'  (güvenli varsayım: kullanıcıya
 *    "bu artık yok" YALANI söylemektense retry öner)
 *
 * NOT: Cihazın gerçekten çevrimdışı olup olmadığı (uçak modu vb.) axios hata nesnesinin
 * şeklinden ayırt edilemez — hem "cihaz offline" hem "sunucu geçici erişilemez" durumunda
 * axios aynı ERR_NETWORK/timeout sinyalini üretir. Gerçek cihaz-offline algısı için NetInfo/
 * expo-network gibi ayrı bir native modül gerekir (bu değişiklikte YOK — bkz. PR raporu).
 */
export type ErrorKind = 'notFound' | 'network';

export function classifyError(error: unknown): ErrorKind {
  if (!axios.isAxiosError(error)) return 'network';
  if (error.response?.status === 404) return 'notFound';
  return 'network';
}
