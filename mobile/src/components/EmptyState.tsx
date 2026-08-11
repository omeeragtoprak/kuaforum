import { View, Text, StyleSheet } from 'react-native';
import { Colors, Radius, FontSize, FontWeight, Spacing } from '@/lib/theme';
import { Button } from './Button';
import { Icon, IconName } from './Icon';

/**
 * Bu bileşen 17 ekranda `emoji="..."` ile çağrılıyor (app/ altında — bu dönüşümün
 * kapsamı dışında, DOKUNULMADI). Var olan çağrıları kırmadan emoji'den çizgi ikona
 * geçmek için: ekranlardan gelen emoji karakteri burada bilinen bir `IconName`'e
 * eşleniyor. Eşleşme yoksa (yeni/bilinmeyen bir emoji) sessizce eski emoji metnine
 * düşülür — ekran hiç boş kalmaz, sadece henüz "Vitrin"e taşınmamış görünür.
 * Yeni çağrılar `icon` prop'unu doğrudan geçebilir (emoji'ye hiç ihtiyaç duymaz).
 */
const EMOJI_TO_ICON: Record<string, IconName> = {
  '⚠️': 'alert',
  '🏪': 'store',
  '📡': 'globe',
  '✂️': 'scissors',
  '📅': 'calendar',
  '🗓️': 'calendar',
  '🚫': 'x',
  '🔒': 'lock',
  '🔎': 'search',
  '🔍': 'search',
  '💬': 'document',
  '☕': 'calendar',
};

type EmptyStateProps = {
  /** @deprecated Yeni çağrılarda `icon` kullan. Eski emoji çağrıları eşlenerek Icon'a çevrilir. */
  emoji?: string;
  /** Yeni tercih edilen yol — Icon.tsx'teki 30 çizgi ikondan biri. */
  icon?: IconName;
  title: string;
  description?: string;
  actionLabel?: string;
  onAction?: () => void;
};

export function EmptyState({ emoji, icon, title, description, actionLabel, onAction }: EmptyStateProps) {
  const resolvedIcon = icon ?? (emoji ? EMOJI_TO_ICON[emoji] : undefined);

  return (
    <View style={styles.container}>
      {resolvedIcon ? (
        <View style={styles.iconWrap} accessible={false}>
          <Icon name={resolvedIcon} size={28} color={Colors.textSecondary} strokeWidth={1.5} />
        </View>
      ) : emoji ? (
        // Eşlenmemiş eski emoji — geçici geri düşüş, ekran boş kalmasın.
        <Text style={styles.emoji}>{emoji}</Text>
      ) : null}
      <Text style={styles.title}>{title}</Text>
      {description ? <Text style={styles.description}>{description}</Text> : null}
      {actionLabel && onAction ? (
        <View style={styles.action}>
          <Button label={actionLabel} variant="outline" size="md" fullWidth={false} onPress={onAction} />
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { alignItems: 'center', paddingVertical: Spacing.xxl, paddingHorizontal: Spacing.lg },
  iconWrap: {
    width: 64,
    height: 64,
    borderRadius: Radius.full,
    backgroundColor: Colors.surfaceAlt,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.md,
  },
  emoji: { fontSize: 56, marginBottom: Spacing.md },
  title: {
    fontSize: FontSize.lg,
    fontWeight: FontWeight.semibold,
    color: Colors.text,
    marginBottom: Spacing.xs,
    textAlign: 'center',
  },
  description: {
    fontSize: FontSize.sm,
    color: Colors.textSecondary,
    textAlign: 'center',
    lineHeight: 20,
    maxWidth: 320,
  },
  action: { marginTop: Spacing.lg },
});
