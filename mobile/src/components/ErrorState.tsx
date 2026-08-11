import { EmptyState } from '@/components/EmptyState';
import { IconName } from '@/components/Icon';
import { classifyError } from '@/lib/classify-error';

type ErrorStateProps = {
  /** TanStack Query'nin `error` alanı — genelde bir AxiosError. */
  error: unknown;
  /** 'network' sınıfında "Tekrar dene" butonuna basınca çağrılır (örn. `query.refetch`). */
  onRetry: () => void;

  /** 404 / kayıt-yok durumunun ekrana özgü metni. Tekrar dene YOK — anlamsız, kayıt zaten yok. */
  notFoundTitle: string;
  notFoundDescription: string;
  notFoundActionLabel: string;
  onNotFoundAction: () => void;
  /** @deprecated Yerine `notFoundIcon` kullan. Geriye dönük — EmptyState eski emoji'yi eşler. */
  notFoundEmoji?: string;
  /** Tercih edilen yol — bkz. Icon.tsx. Verilmezse `notFoundEmoji` (varsayılan 'x' ikonuna eşlenir). */
  notFoundIcon?: IconName;

  /** 'network' (5xx / timeout / DNS / bağlantı yok) durumunun metni — makul bir varsayılanı var. */
  networkTitle?: string;
  networkDescription?: string;
  /** @deprecated Yerine `networkIcon` kullan. Geriye dönük — EmptyState eski emoji'yi eşler. */
  networkEmoji?: string;
  /** Tercih edilen yol — bkz. Icon.tsx. Verilmezse `networkEmoji` (varsayılan 'globe' ikonuna eşlenir). */
  networkIcon?: IconName;
};

/**
 * G12 fix: 404 (kayıt gerçekten yok) ile ağ hatası (sunucu geçici erişilemez — retry mantıklı)
 * artık aynı ekranda birleşmiyor. Ayrım `classifyError`'dan gelir (bkz. `src/lib/classify-error.ts`
 * — karar tablosu orada). Bu bileşen tek görevi olan "hangi metin + hangi aksiyon" kararını
 * merkezileştirir; ekranlar sadece kendi metinlerini geçirir, mantığı kopyalamaz.
 */
export function ErrorState({
  error,
  onRetry,
  notFoundTitle,
  notFoundDescription,
  notFoundActionLabel,
  onNotFoundAction,
  notFoundEmoji = '🚫',
  notFoundIcon,
  networkTitle = 'Bağlantı kurulamadı',
  networkDescription = 'Sunucuya ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene.',
  networkEmoji = '📡',
  networkIcon,
}: ErrorStateProps) {
  const kind = classifyError(error);

  if (kind === 'notFound') {
    return (
      <EmptyState
        icon={notFoundIcon}
        emoji={notFoundEmoji}
        title={notFoundTitle}
        description={notFoundDescription}
        actionLabel={notFoundActionLabel}
        onAction={onNotFoundAction}
      />
    );
  }

  return (
    <EmptyState
      icon={networkIcon}
      emoji={networkEmoji}
      title={networkTitle}
      description={networkDescription}
      actionLabel="Tekrar dene"
      onAction={onRetry}
    />
  );
}
