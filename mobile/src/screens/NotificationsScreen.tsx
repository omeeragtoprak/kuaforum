import { View, Text, StyleSheet, FlatList, Pressable, RefreshControl, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { EmptyState } from '@/components/EmptyState';
import { Icon, IconName } from '@/components/Icon';
import { SkeletonList, SkeletonBookingRow } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, Spacing, Layout, Radius } from '@/lib/theme';
import { NotificationApi, NotificationFeedItem } from '@/api/notification';
import { extractApiError } from '@/api/client';

function formatRelative(iso: string): string {
  const now = Date.now();
  const t = new Date(iso).getTime();
  const sec = Math.max(0, Math.floor((now - t) / 1000));
  if (sec < 60) return 'şimdi';
  if (sec < 3600) return `${Math.floor(sec / 60)} dk önce`;
  if (sec < 86400) return `${Math.floor(sec / 3600)} sa önce`;
  if (sec < 604800) return `${Math.floor(sec / 86400)} gün önce`;
  const d = new Date(iso);
  return d.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' });
}

function templateIcon(key: string): IconName {
  if (key.startsWith('booking.confirmed')) return 'check';
  if (key.startsWith('booking.cancelled')) return 'x';
  if (key.startsWith('booking.started')) return 'play';
  if (key.startsWith('booking.completed')) return 'checkCircle';
  if (key.startsWith('booking.no_show')) return 'alert';
  if (key.startsWith('payment')) return 'creditCard';
  if (key.startsWith('review')) return 'star';
  return 'bell';
}

/** İkon rengi bildirim türünün anlamını taşır (iptal=error, tamamlandı/onaylandı=success,
 *  ödeme/başladı=info, no-show=warning) — bu renk YALNIZ rozet/ikonda kalır, konu/gövde
 *  metnine uygulanmaz (metin her zaman nötr Colors.text/textSecondary). */
function templateIconColor(key: string): string {
  if (key.startsWith('booking.confirmed')) return Colors.success;
  if (key.startsWith('booking.cancelled')) return Colors.error;
  if (key.startsWith('booking.started')) return Colors.info;
  if (key.startsWith('booking.completed')) return Colors.success;
  if (key.startsWith('booking.no_show')) return Colors.warning;
  if (key.startsWith('payment')) return Colors.info;
  if (key.startsWith('review')) return Colors.star;
  return Colors.textSecondary;
}

export function NotificationsScreen({ accent }: { accent: string }) {
  const qc = useQueryClient();
  const router = useRouter();

  const query = useQuery({
    queryKey: ['notifications'],
    queryFn: () => NotificationApi.getMine(50),
  });

  const markRead = useMutation({
    mutationFn: (id: string) => NotificationApi.markRead(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['notifications'] });
      qc.invalidateQueries({ queryKey: ['notifications-unread'] });
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const markAllRead = useMutation({
    mutationFn: () => NotificationApi.markAllRead(),
    onSuccess: (count) => {
      qc.invalidateQueries({ queryKey: ['notifications'] });
      qc.invalidateQueries({ queryKey: ['notifications-unread'] });
      if (count > 0) Alert.alert('Tamam', `${count} bildirim okundu olarak işaretlendi.`);
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const unread = query.data?.unreadCount ?? 0;
  const items = query.data?.items ?? [];

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <View style={{ flex: 1 }}>
          <Text style={styles.title}>Bildirimler</Text>
          <Text style={styles.subtitle}>
            {unread > 0 ? `${unread} okunmamış` : 'Hepsi okundu'}
          </Text>
        </View>
        {unread > 0 ? (
          <Pressable
            onPress={() => markAllRead.mutate()}
            style={({ pressed }) => [styles.markAllBtn, { borderColor: accent }, pressed && { opacity: 0.7 }]}
          >
            <Text style={[styles.markAllText, { color: accent }]}>Hepsini okundu işaretle</Text>
          </Pressable>
        ) : null}
      </View>

      <FlatList
        data={items}
        keyExtractor={(it) => it.id}
        contentContainerStyle={styles.scroll}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={query.isFetching && !query.isLoading}
            onRefresh={() => query.refetch()}
            tintColor={accent}
          />
        }
        ListEmptyComponent={
          query.isLoading ? (
            // G11 fix: eskiden yükleme sırasında liste alanı tamamen boştu (null) —
            // başlık görünür ama içerik alanı "beyaz ekran" gibi kalıyordu.
            <SkeletonList count={4} render={() => <SkeletonBookingRow />} />
          ) : query.isError ? (
            <EmptyState
              icon="alert"
              title="Bağlantı hatası"
              description="Bildirimler yüklenemedi."
              actionLabel="Tekrar dene"
              onAction={() => query.refetch()}
            />
          ) : (
            <EmptyState icon="bell" title="Bildirim yok" description="Yeni bir aktivite olduğunda burada görünecek." />
          )
        }
        renderItem={({ item }) => (
          <NotificationRow
            item={item}
            accent={accent}
            onPress={() => {
              if (!item.isRead) markRead.mutate(item.id);
              if (item.relatedBookingId) router.push(`/booking/${item.relatedBookingId}` as never);
            }}
          />
        )}
      />
    </SafeAreaView>
  );
}

function NotificationRow({
  item,
  accent,
  onPress,
}: {
  item: NotificationFeedItem;
  accent: string;
  onPress: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={`${item.subject}. ${item.body}. ${item.isRead ? 'Okundu' : 'Okunmadı'}`}
      accessibilityState={{ selected: !item.isRead }}
      style={({ pressed }) => [pressed && { opacity: 0.7 }]}
    >
      <Card style={!item.isRead ? { ...styles.row, borderColor: accent, borderWidth: 1.5 } : styles.row}>
        <View style={styles.iconBadge}>
          <Icon name={templateIcon(item.templateKey)} size={20} color={templateIconColor(item.templateKey)} strokeWidth={1.6} />
          {!item.isRead ? <View style={[styles.dot, { backgroundColor: accent }]} /> : null}
        </View>
        <View style={{ flex: 1, gap: 4 }}>
          <Text style={[styles.subject, !item.isRead && { fontWeight: FontWeight.bold }]} numberOfLines={1}>
            {item.subject}
          </Text>
          <Text style={styles.body} numberOfLines={2}>
            {item.body}
          </Text>
          <Text style={styles.time}>{formatRelative(item.createdAt)}</Text>
        </View>
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.lg,
    paddingBottom: Spacing.sm,
    gap: Spacing.md,
  },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 2 },
  markAllBtn: {
    paddingHorizontal: Spacing.md,
    paddingVertical: 8,
    borderRadius: Radius.md,
    borderWidth: 1.5,
  },
  markAllText: { fontSize: FontSize.xs, fontWeight: FontWeight.semibold },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    gap: Spacing.sm,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
    flexGrow: 1,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: Spacing.md,
  },
  iconBadge: {
    width: 44,
    height: 44,
    borderRadius: Radius.lg,
    backgroundColor: Colors.surfaceAlt,
    alignItems: 'center',
    justifyContent: 'center',
    position: 'relative',
  },
  dot: {
    position: 'absolute',
    top: 0,
    right: 0,
    width: 10,
    height: 10,
    borderRadius: 5,
    borderWidth: 2,
    borderColor: Colors.surface,
  },
  subject: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.semibold },
  body: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 20 },
  time: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
});
