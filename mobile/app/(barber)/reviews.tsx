import { useState } from 'react';
import {
  View, Text, StyleSheet, FlatList, RefreshControl, Pressable,
  Modal, KeyboardAvoidingView, Platform, Alert,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { EmptyState } from '@/components/EmptyState';
import { Icon } from '@/components/Icon';
import { SkeletonBookingRow, SkeletonList } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, LetterSpacing, Radius, Spacing, Layout } from '@/lib/theme';
import { BarberApi } from '@/api/barber';
import { ReviewApi, Review } from '@/api/review';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';

export default function BarberReviewsScreen() {
  const router = useRouter();
  const qc = useQueryClient();

  const shopQuery = useQuery({ queryKey: ['my-shop'], queryFn: () => BarberApi.getMyShop() });
  const shopId = shopQuery.data?.id;

  const reviewsQuery = useQuery({
    queryKey: ['my-shop-reviews', shopId],
    queryFn: () => ReviewApi.getShopReviews(shopId!, 1, 50),
    enabled: !!shopId,
  });

  const [replyTarget, setReplyTarget] = useState<Review | null>(null);
  const [replyBody, setReplyBody] = useState('');

  const replyMut = useMutation({
    mutationFn: () => ReviewApi.reply(replyTarget!.id, replyBody.trim()),
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-shop-reviews', shopId] });
      setReplyTarget(null);
      setReplyBody('');
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const items = reviewsQuery.data?.items ?? [];
  const summary = reviewsQuery.data?.summary;
  const replyValid = replyBody.trim().length >= 3 && replyBody.trim().length <= 500;

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <Pressable
          onPress={() => router.back()}
          style={styles.backBtn}
          hitSlop={8}
          accessibilityRole="button"
          accessibilityLabel="Geri dön"
        >
          <Icon name="chevronLeft" size={18} color={Colors.ink} strokeWidth={2} />
          <Text style={styles.back}>Geri</Text>
        </Pressable>
        <Text style={styles.title}>Yorumlar</Text>
        {summary ? (
          <View style={styles.summaryRow}>
            <Text style={styles.stars}>{'★'.repeat(Math.round(summary.average))}{'☆'.repeat(5 - Math.round(summary.average))}</Text>
            <Text style={styles.summaryText}>
              {summary.count > 0 ? `${summary.average.toFixed(1)} · ${summary.count} yorum` : 'Henüz yorum yok'}
            </Text>
          </View>
        ) : null}
      </View>

      <FlatList
        data={items}
        keyExtractor={(r) => r.id}
        contentContainerStyle={styles.list}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl
            refreshing={reviewsQuery.isFetching && !reviewsQuery.isLoading}
            onRefresh={() => reviewsQuery.refetch()}
            tintColor={Colors.ink}
          />
        }
        ItemSeparatorComponent={() => <View style={{ height: Spacing.md }} />}
        ListEmptyComponent={
          shopQuery.isLoading || reviewsQuery.isLoading ? (
            <SkeletonList count={3} render={() => <SkeletonBookingRow />} />
          ) : shopQuery.isError ? (
            // G12 fix: shopQuery hatası (ağ/5xx) eskiden `!shopId` dalına düşüp "salon yok"
            // YALANI söylüyordu — `getMyShop()` artık gerçek hatayı yutmuyor (bkz. api/barber.ts),
            // burada da ayrı dal şart.
            <EmptyState
              icon="globe"
              title="Bağlantı kurulamadı"
              description="Salon bilgine ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene."
              actionLabel="Tekrar dene"
              onAction={() => shopQuery.refetch()}
            />
          ) : !shopId ? (
            <EmptyState
              icon="store"
              title="Salon yok"
              description="Önce Profil sekmesinden salon bilgilerini girmelisin."
            />
          ) : reviewsQuery.isError ? (
            <EmptyState
              icon="alert"
              title="Bağlantı hatası"
              description="Yorumlar yüklenemedi."
              actionLabel="Tekrar dene"
              onAction={() => reviewsQuery.refetch()}
            />
          ) : (
            <EmptyState
              icon="document"
              title="Henüz yorum yok"
              description="Müşterilerin randevuyu tamamlayınca yorumları burada görüneceksin."
            />
          )
        }
        renderItem={({ item }) => (
          <Card style={{ gap: Spacing.sm }}>
            <View style={styles.reviewHead}>
              <View>
                <Text style={styles.stars}>{'★'.repeat(item.rating)}{'☆'.repeat(5 - item.rating)}</Text>
                <Text style={styles.dateText}>{new Date(item.createdAt).toLocaleDateString('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' })}</Text>
              </View>
              <Text style={styles.ratingValue}>{item.rating}/5</Text>
            </View>
            {item.body ? (
              <Text style={styles.body}>{item.body}</Text>
            ) : (
              <Text style={styles.bodyEmpty}>(Sadece yıldız puan — metin yok)</Text>
            )}

            {item.replyBody ? (
              <View style={styles.replyBox}>
                <Text style={styles.replyLabel}>İşletme cevabı</Text>
                <Text style={styles.replyText}>{item.replyBody}</Text>
                {item.replyAt ? (
                  <Text style={styles.replyDate}>{new Date(item.replyAt).toLocaleDateString('tr-TR')}</Text>
                ) : null}
              </View>
            ) : (
              <Pressable
                style={styles.replyBtn}
                onPress={() => { setReplyTarget(item); setReplyBody(''); }}
                accessibilityRole="button"
              >
                <Icon name="document" size={14} color={Colors.accentInk} />
                <Text style={styles.replyBtnText}>Cevap yaz</Text>
              </Pressable>
            )}
          </Card>
        )}
      />

      <Modal
        visible={!!replyTarget}
        transparent
        animationType="slide"
        onRequestClose={() => setReplyTarget(null)}
      >
        <KeyboardAvoidingView
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
          style={styles.modalOverlay}
        >
          <Pressable style={{ flex: 1 }} onPress={() => setReplyTarget(null)} />
          <View style={styles.modalSheet}>
            <View style={styles.handle} />
            <Text style={styles.modalTitle}>Yorumu yanıtla</Text>
            {replyTarget ? (
              <View style={styles.modalContext}>
                <Text style={styles.stars}>{'★'.repeat(replyTarget.rating)}{'☆'.repeat(5 - replyTarget.rating)}</Text>
                <Text style={styles.modalContextBody} numberOfLines={3}>
                  {replyTarget.body ?? '(metin yok)'}
                </Text>
              </View>
            ) : null}
            <Input
              label="Yanıtın"
              value={replyBody}
              onChangeText={setReplyBody}
              placeholder="Müşteriye teşekkürlerin veya açıklaman…"
              multiline
              numberOfLines={4}
              maxLength={500}
              hint={`${replyBody.length}/500 (en az 3 karakter)`}
            />
            <View style={styles.modalHintRow}>
              <Icon name="info" size={13} color={Colors.textMuted} />
              <Text style={styles.modalHint}>
                Yanıtın herkese açıktır ve düzenlenemez. Sadece bir kez cevap verebilirsin.
              </Text>
            </View>
            <View style={styles.modalActions}>
              <Button
                label="Vazgeç"
                variant="secondary"
                onPress={() => setReplyTarget(null)}
                fullWidth={false}
              />
              <Button
                label="Cevapla"
                onPress={() => replyMut.mutate()}
                loading={replyMut.isPending}
                disabled={!replyValid}
                fullWidth={false}
              />
            </View>
          </View>
        </KeyboardAvoidingView>
      </Modal>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { paddingHorizontal: Layout.screenPaddingX, paddingTop: Spacing.md, paddingBottom: Spacing.sm },
  backBtn: { flexDirection: 'row', alignItems: 'center', gap: 2, alignSelf: 'flex-start', paddingVertical: 4, marginLeft: -4 },
  back: { color: Colors.ink, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, marginTop: 6, letterSpacing: LetterSpacing.title },
  summaryRow: { flexDirection: 'row', alignItems: 'center', gap: 8, marginTop: 8 },
  summaryText: { fontSize: FontSize.sm, color: Colors.textSecondary },
  list: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  reviewHead: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start' },
  stars: { fontSize: 18, color: Colors.star, letterSpacing: 2 },
  ratingValue: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  dateText: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 4 },
  body: { fontSize: FontSize.sm, color: Colors.text, lineHeight: 20 },
  bodyEmpty: { fontSize: FontSize.sm, color: Colors.textMuted, fontStyle: 'italic' },
  replyBox: {
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.sm,
    padding: Spacing.sm,
    borderLeftWidth: 3,
    borderLeftColor: Colors.ink,
  },
  replyLabel: { fontSize: FontSize.xs, color: Colors.ink, fontWeight: FontWeight.bold, letterSpacing: LetterSpacing.label },
  replyText: { fontSize: FontSize.sm, color: Colors.text, marginTop: 4, lineHeight: 20 },
  replyDate: { fontSize: 10, color: Colors.textMuted, marginTop: 4 },
  replyBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    backgroundColor: Colors.accentSoft,
    paddingHorizontal: Spacing.md,
    paddingVertical: 10,
    borderRadius: Radius.sm,
    alignSelf: 'flex-start',
  },
  replyBtnText: { color: Colors.accentInk, fontSize: FontSize.sm, fontWeight: FontWeight.semibold },

  modalOverlay: { flex: 1, backgroundColor: Colors.overlay, justifyContent: 'flex-end' },
  modalSheet: {
    backgroundColor: Colors.background,
    borderTopLeftRadius: Radius.lg,
    borderTopRightRadius: Radius.lg,
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: 12,
    paddingBottom: Spacing.xl,
    gap: Spacing.md,
  },
  handle: { width: 40, height: 4, borderRadius: 2, backgroundColor: Colors.border, alignSelf: 'center', marginBottom: 8 },
  modalTitle: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  modalContext: {
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.sm,
    padding: Spacing.sm,
    gap: 4,
  },
  modalContextBody: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 18 },
  modalHintRow: { flexDirection: 'row', alignItems: 'flex-start', gap: 6 },
  modalHint: { flex: 1, fontSize: FontSize.xs, color: Colors.textMuted, lineHeight: 18 },
  modalActions: { flexDirection: 'row', gap: Spacing.sm, justifyContent: 'flex-end' },
});
