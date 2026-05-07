import { useEffect, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Alert, Pressable, TextInput, Linking } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, FontWeight, Radius, Spacing, Layout } from '@/lib/theme';
import { BookingApi } from '@/api/booking';
import { extractApiError } from '@/api/client';
import {
  formatDateTime, formatDuration, formatTry, formatPhoneTr,
  bookingStatusInfo, isBookingUpcoming,
  isBookingPending, isBookingConfirmed, isBookingInProgress, isBookingCompleted,
} from '@/lib/format';
import { useAuthStore } from '@/store/auth-store';

export default function BookingDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const qc = useQueryClient();

  const query = useQuery({
    queryKey: ['booking', id],
    queryFn: () => BookingApi.getById(id!),
    enabled: !!id,
  });

  const isBarber = useAuthStore((s) => s.isBarber());

  const [showRescheduleForm, setShowRescheduleForm] = useState(false);
  const [newDateInput, setNewDateInput] = useState('');
  const [newTimeInput, setNewTimeInput] = useState('');

  const rescheduleMutation = useMutation({
    mutationFn: (newScheduledAt: string) => BookingApi.reschedule(id!, newScheduledAt),
    onSuccess: () => {
      Alert.alert('Yeniden planlandı', 'Randevu yeni saate taşındı.');
      setShowRescheduleForm(false);
      qc.invalidateQueries({ queryKey: ['booking', id] });
      qc.invalidateQueries({ queryKey: ['my-bookings'] });
      qc.invalidateQueries({ queryKey: ['owner-bookings-2weeks'] });
      qc.invalidateQueries({ queryKey: ['notifications-unread'] });
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const cancelMutation = useMutation({
    mutationFn: (reason: number) => BookingApi.cancel(id!, reason),
    onSuccess: (data) => {
      Alert.alert('İptal edildi', `İade oranı: %${data.refundPercentage}`);
      qc.invalidateQueries({ queryKey: ['booking', id] });
      qc.invalidateQueries({ queryKey: ['my-bookings'] });
      qc.invalidateQueries({ queryKey: ['owner-bookings-2weeks'] });
      qc.invalidateQueries({ queryKey: ['notifications-unread'] });
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const ownerConfirmMutation = useMutation({
    mutationFn: () => BookingApi.ownerConfirm(id!),
    onSuccess: () => {
      Alert.alert('Onaylandı', 'Randevu onaylandı, müşteriye bildirim gönderildi.');
      qc.invalidateQueries({ queryKey: ['booking', id] });
      qc.invalidateQueries({ queryKey: ['owner-bookings-2weeks'] });
      qc.invalidateQueries({ queryKey: ['notifications-unread'] });
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const startMutation = useMutation({
    mutationFn: () => BookingApi.start(id!),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['booking', id] });
      qc.invalidateQueries({ queryKey: ['owner-bookings-2weeks'] });
      qc.invalidateQueries({ queryKey: ['notifications-unread'] });
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const completeMutation = useMutation({
    mutationFn: () => BookingApi.complete(id!),
    onSuccess: () => {
      Alert.alert('Tamamlandı', 'Randevu tamamlandı. Müşteri yorum yapabilir.');
      qc.invalidateQueries({ queryKey: ['booking', id] });
      qc.invalidateQueries({ queryKey: ['owner-bookings-2weeks'] });
      qc.invalidateQueries({ queryKey: ['notifications-unread'] });
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  const noShowMutation = useMutation({
    mutationFn: () => BookingApi.markNoShow(id!),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['booking', id] });
      qc.invalidateQueries({ queryKey: ['owner-bookings-2weeks'] });
      qc.invalidateQueries({ queryKey: ['notifications-unread'] });
    },
    onError: (err) => Alert.alert('Hata', extractApiError(err)),
  });

  function handleCancel() {
    const reasonCode = isBarber ? 5 : 0;
    Alert.alert(
      'Randevuyu iptal et',
      isBarber
        ? 'Kuaför iptali — müşteri %100 iade alır.'
        : 'Kademeli iade uygulanır:\n• 24 saat öncesine kadar: %100\n• 12-24 saat: %50\n• 0-12 saat: iade yok',
      [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'İptal et', style: 'destructive', onPress: () => cancelMutation.mutate(reasonCode) },
      ],
    );
  }

  function handleStart() {
    if (!query.data) return;
    const minutesUntil = Math.round((new Date(query.data.scheduledAt).getTime() - Date.now()) / 60000);
    if (minutesUntil > 15) {
      Alert.alert(
        'Erken başlatma',
        `Randevu saatine ${minutesUntil} dakika var. Yine de başlatmak istiyor musun?`,
        [
          { text: 'Vazgeç', style: 'cancel' },
          { text: 'Yine de başlat', onPress: () => startMutation.mutate() },
        ],
      );
      return;
    }
    startMutation.mutate();
  }

  function handleComplete() {
    if (!query.data) return;
    Alert.alert(
      'Hizmeti tamamla',
      `${formatDateTime(query.data.scheduledAt)} randevusunu tamamlamak istediğine emin misin? Müşteriye "Tamamlandı" bildirimi gidecek.`,
      [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'Tamamla', onPress: () => completeMutation.mutate() },
      ],
    );
  }

  function handleOwnerConfirm() {
    Alert.alert('Onayla', 'Randevuyu onaylamak istediğine emin misin?', [
      { text: 'Vazgeç', style: 'cancel' },
      { text: 'Onayla', onPress: () => ownerConfirmMutation.mutate() },
    ]);
  }

  function handleNoShow() {
    Alert.alert('Gelmedi', 'Müşteri gelmedi olarak işaretlensin mi? İade yapılmaz.', [
      { text: 'Vazgeç', style: 'cancel' },
      { text: 'Gelmedi', style: 'destructive', onPress: () => noShowMutation.mutate() },
    ]);
  }

  if (query.isLoading) {
    return <SafeAreaView style={styles.container}><EmptyState title="Yükleniyor…" /></SafeAreaView>;
  }
  if (query.isError || !query.data) {
    return (
      <SafeAreaView style={styles.container}>
        <EmptyState
          emoji="⚠️"
          title="Bulunamadı"
          description="Bu randevu yüklenemedi."
          actionLabel="Geri dön"
          onAction={() => router.back()}
        />
      </SafeAreaView>
    );
  }

  const b = query.data;
  const status = bookingStatusInfo(b.status);
  const canCancel = isBookingUpcoming(b.status);
  const canReview = !isBarber && isBookingCompleted(b.status) && !b.hasReview;
  const alreadyReviewed = !isBarber && isBookingCompleted(b.status) && !!b.hasReview;
  const ownerCanConfirm = isBarber && isBookingPending(b.status);
  const ownerCanStart = isBarber && isBookingConfirmed(b.status);
  const ownerCanComplete = isBarber && isBookingInProgress(b.status);
  const ownerCanNoShow = isBarber && isBookingConfirmed(b.status) && new Date(b.scheduledEndsAt).getTime() < Date.now();
  const canReschedule = (isBookingPending(b.status) || isBookingConfirmed(b.status));
  const totalDuration = b.items.reduce((s, i) => s + (i.durationMinutes ?? 0), 0);

  function startReschedule() {
    const current = new Date(b!.scheduledAt);
    const yyyy = current.getFullYear();
    const mm = String(current.getMonth() + 1).padStart(2, '0');
    const dd = String(current.getDate()).padStart(2, '0');
    const hh = String(current.getHours()).padStart(2, '0');
    const mn = String(current.getMinutes()).padStart(2, '0');
    setNewDateInput(`${yyyy}-${mm}-${dd}`);
    setNewTimeInput(`${hh}:${mn}`);
    setShowRescheduleForm(true);
  }

  function submitReschedule() {
    const parts = newDateInput.match(/^(\d{4})-(\d{2})-(\d{2})$/);
    const tparts = newTimeInput.match(/^(\d{2}):(\d{2})$/);
    if (!parts || !tparts) {
      Alert.alert('Geçersiz format', 'Tarih: YYYY-MM-DD, saat: HH:MM');
      return;
    }
    const [, y, mo, d] = parts;
    const [, hh, mn] = tparts;
    const dt = new Date(Number(y), Number(mo) - 1, Number(d), Number(hh), Number(mn), 0, 0);
    if (isNaN(dt.getTime()) || dt.getTime() <= Date.now() + 5 * 60 * 1000) {
      Alert.alert('Geçersiz tarih', 'En az 5 dakika sonrası seçmelisin.');
      return;
    }
    Alert.alert(
      'Yeniden planla',
      `Yeni saat: ${dt.toLocaleString('tr-TR')}`,
      [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'Onayla', onPress: () => rescheduleMutation.mutate(dt.toISOString()) },
      ],
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <ScrollView
        contentContainerStyle={styles.scroll}
        showsVerticalScrollIndicator={false}
      >
        <Pressable onPress={() => router.back()} style={styles.backBtn}>
          <Text style={styles.back}>← Geri</Text>
        </Pressable>

        <View style={styles.headerBlock}>
          <Text style={styles.title}>Randevu</Text>
          <View style={styles.headerStatusRow}>
            <Badge label={status.label} tone={status.tone} />
            {(isBookingPending(b.status) || isBookingConfirmed(b.status)) ? (
              <CountdownBadge scheduledAt={b.scheduledAt} />
            ) : null}
          </View>
          <Text style={styles.dateText}>{formatDateTime(b.scheduledAt)}</Text>
          <Text style={styles.durationText}>{formatDuration(totalDuration)} · {b.items.length} hizmet</Text>
        </View>

        <Text style={styles.sectionTitle}>Detay</Text>
        <Card style={{ gap: 0 }} padded={false}>
          {b.shopName ? <><DetailRow icon="🏪" label="Salon" value={b.shopName} /><Divider /></> : null}
          {b.stylistName ? <><DetailRow icon="✂️" label="Stilist" value={b.stylistName} /><Divider /></> : null}
          <DetailRow icon="📅" label="Başlangıç" value={formatDateTime(b.scheduledAt)} />
          <Divider />
          <DetailRow icon="⏱️" label="Bitiş" value={formatDateTime(b.scheduledEndsAt)} />
          <Divider />
          <DetailRow icon="💰" label="Toplam" value={formatTry(b.totalAmountMinor)} />
          {b.refundPercentage != null ? (
            <>
              <Divider />
              <DetailRow icon="↩️" label="İade oranı" value={`%${b.refundPercentage}`} />
            </>
          ) : null}
        </Card>

        {isBarber && (b.customerName || b.customerPhone || b.customerNote) ? (
          <>
            <Text style={styles.sectionTitle}>Müşteri</Text>
            <Card style={{ gap: Spacing.sm }}>
              {b.customerName ? (
                <Text style={styles.customerNameText}>👤 {b.customerName}</Text>
              ) : null}
              {b.customerPhone ? (
                <Pressable
                  onPress={() => Linking.openURL(`tel:${b.customerPhone}`)}
                  hitSlop={6}
                  accessibilityRole="link"
                  accessibilityLabel={`Müşteriyi ara ${b.customerPhone}`}
                >
                  <Text style={styles.customerPhoneText}>📞 {formatPhoneTr(b.customerPhone)}</Text>
                </Pressable>
              ) : null}
              {b.customerNote ? (
                <View style={styles.customerNoteBox}>
                  <Text style={styles.customerNoteLabel}>NOT</Text>
                  <Text style={styles.customerNoteText}>{b.customerNote}</Text>
                </View>
              ) : null}
            </Card>
          </>
        ) : null}

        <Text style={styles.sectionTitle}>Hizmetler</Text>
        <Card style={{ gap: 0 }} padded={false}>
          {b.items.map((it, idx) => (
            <View key={it.id}>
              <View style={styles.itemRow}>
                <View style={{ flex: 1 }}>
                  <Text style={styles.itemName}>{it.serviceName}</Text>
                  <Text style={styles.itemMeta}>{formatDuration(it.durationMinutes)}</Text>
                </View>
                <Text style={styles.itemPrice}>{formatTry(it.priceMinor)}</Text>
              </View>
              {idx < b.items.length - 1 ? <Divider noOffset /> : null}
            </View>
          ))}
        </Card>

        {showRescheduleForm ? (
          <Card>
            <Text style={[styles.sectionTitle, { marginTop: 0, paddingHorizontal: 0 }]}>Yeni randevu zamanı</Text>
            <Text style={styles.dateText}>Tarih (YYYY-MM-DD)</Text>
            <TextInput
              value={newDateInput}
              onChangeText={setNewDateInput}
              placeholder="2026-05-20"
              autoCapitalize="none"
              keyboardType="numbers-and-punctuation"
              style={styles.textInput}
            />
            <Text style={[styles.dateText, { marginTop: 8 }]}>Saat (HH:MM)</Text>
            <TextInput
              value={newTimeInput}
              onChangeText={setNewTimeInput}
              placeholder="14:30"
              autoCapitalize="none"
              keyboardType="numbers-and-punctuation"
              style={styles.textInput}
            />
            <View style={{ flexDirection: 'row', gap: 8, marginTop: 12 }}>
              <Button label="İptal" variant="outline" fullWidth onPress={() => setShowRescheduleForm(false)} />
              <Button label="Onayla" fullWidth onPress={submitReschedule} loading={rescheduleMutation.isPending} />
            </View>
          </Card>
        ) : null}

        <View style={styles.actions}>
          {ownerCanConfirm ? (
            <Button
              label="✅ Randevuyu onayla"
              onPress={handleOwnerConfirm}
              loading={ownerConfirmMutation.isPending}
            />
          ) : null}
          {ownerCanStart ? (
            <Button
              label="▶️ Başlat"
              onPress={handleStart}
              loading={startMutation.isPending}
            />
          ) : null}
          {ownerCanComplete ? (
            <Button
              label="🏁 Tamamla"
              onPress={handleComplete}
              loading={completeMutation.isPending}
            />
          ) : null}
          {ownerCanNoShow ? (
            <Button
              label="Gelmedi olarak işaretle"
              variant="destructive"
              onPress={handleNoShow}
              loading={noShowMutation.isPending}
            />
          ) : null}
          {canReschedule && !showRescheduleForm ? (
            <Button
              label="🗓️ Yeniden planla"
              variant="outline"
              onPress={startReschedule}
              loading={rescheduleMutation.isPending}
            />
          ) : null}
          {canCancel ? (
            <Button
              label="Randevuyu iptal et"
              variant="destructive"
              onPress={handleCancel}
              loading={cancelMutation.isPending}
            />
          ) : null}
          {canReview ? (
            <Button
              label="⭐ Yorum yap"
              onPress={() => router.push({ pathname: '/review/submit', params: { bookingId: b.id } })}
            />
          ) : null}
          {alreadyReviewed ? (
            <View style={styles.reviewDoneBox} accessible accessibilityLabel="Bu randevuya yorum yazıldı">
              <Text style={styles.reviewDoneText}>✅ Bu randevu için yorumun alındı, teşekkürler.</Text>
            </View>
          ) : null}
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

function CountdownBadge({ scheduledAt }: { scheduledAt: string }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 60_000);
    return () => clearInterval(t);
  }, []);

  const diffMs = new Date(scheduledAt).getTime() - now;
  if (diffMs <= 0) return null;

  const minutes = Math.floor(diffMs / 60_000);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  let label: string;
  let tone: 'urgent' | 'soon' | 'normal';
  if (minutes < 60) {
    label = `⏱️ ${minutes} dk kaldı`;
    tone = 'urgent';
  } else if (hours < 24) {
    label = `⏱️ ${hours} saat kaldı`;
    tone = 'soon';
  } else if (days < 7) {
    label = `📅 ${days} gün kaldı`;
    tone = 'normal';
  } else {
    label = `📅 ${days} gün kaldı`;
    tone = 'normal';
  }

  return (
    <View style={[
      styles.countdown,
      tone === 'urgent' && styles.countdownUrgent,
      tone === 'soon' && styles.countdownSoon,
    ]}>
      <Text style={[
        styles.countdownText,
        tone === 'urgent' && styles.countdownTextUrgent,
        tone === 'soon' && styles.countdownTextSoon,
      ]}>
        {label}
      </Text>
    </View>
  );
}

function DetailRow({ icon, label, value }: { icon: string; label: string; value: string }) {
  return (
    <View style={styles.detailRow}>
      <Text style={styles.detailIcon}>{icon}</Text>
      <Text style={styles.detailLabel}>{label}</Text>
      <View style={{ flex: 1 }} />
      <Text style={styles.detailValue}>{value}</Text>
    </View>
  );
}

function Divider({ noOffset }: { noOffset?: boolean } = {}) {
  return <View style={[styles.divider, noOffset && { marginLeft: 0 }]} />;
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    gap: Spacing.md,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  backBtn: { paddingVertical: 6, alignSelf: 'flex-start' },
  back: { color: Colors.primary, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  headerBlock: { paddingTop: Spacing.sm, paddingBottom: Spacing.md },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text },
  headerStatusRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginTop: 8 },
  countdown: {
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: Radius.full,
    backgroundColor: Colors.infoSoft,
  },
  reviewDoneBox: {
    backgroundColor: Colors.successSoft,
    borderRadius: Radius.md,
    padding: Spacing.md,
    alignItems: 'center',
  },
  reviewDoneText: { fontSize: FontSize.sm, color: Colors.success, fontWeight: FontWeight.semibold },
  countdownText: { fontSize: FontSize.xs, fontWeight: FontWeight.bold, color: Colors.info },
  countdownSoon: { backgroundColor: Colors.warningSoft },
  countdownTextSoon: { color: Colors.warning },
  countdownUrgent: { backgroundColor: Colors.errorSoft },
  countdownTextUrgent: { color: Colors.error },
  dateText: { fontSize: FontSize.lg, color: Colors.text, fontWeight: FontWeight.semibold, marginTop: Spacing.md },
  durationText: { fontSize: FontSize.sm, color: Colors.textMuted, marginTop: 4 },
  sectionTitle: {
    fontSize: FontSize.xs,
    color: Colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    fontWeight: FontWeight.semibold,
    paddingHorizontal: 4,
    marginTop: Spacing.sm,
    marginBottom: 4,
  },
  detailRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    paddingVertical: 14,
    paddingHorizontal: Spacing.lg,
    minHeight: 52,
  },
  detailIcon: { fontSize: 18, width: 24, textAlign: 'center' },
  detailLabel: { fontSize: FontSize.sm, color: Colors.textSecondary },
  detailValue: { fontSize: FontSize.sm, color: Colors.text, fontWeight: FontWeight.semibold },
  divider: { height: 1, backgroundColor: Colors.divider, marginLeft: 56 },
  itemRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    paddingVertical: 14,
    paddingHorizontal: Spacing.lg,
    minHeight: 52,
  },
  itemName: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.medium },
  itemMeta: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  itemPrice: { fontSize: FontSize.md, color: Colors.primary, fontWeight: FontWeight.bold },
  actions: { gap: Spacing.sm, marginTop: Spacing.lg },
  textInput: {
    borderWidth: 1,
    borderColor: Colors.border,
    borderRadius: Radius.md,
    paddingHorizontal: Spacing.md,
    paddingVertical: 10,
    fontSize: FontSize.md,
    color: Colors.text,
    backgroundColor: Colors.surface,
    marginTop: 4,
  },
  customerNameText: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.semibold },
  customerPhoneText: { fontSize: FontSize.sm, color: '#0EA5E9', fontWeight: FontWeight.medium },
  customerNoteBox: {
    backgroundColor: Colors.primarySoft,
    borderRadius: Radius.sm,
    padding: Spacing.sm,
  },
  customerNoteLabel: {
    fontSize: FontSize.xs,
    color: Colors.primary,
    fontWeight: FontWeight.bold,
    letterSpacing: 0.5,
    marginBottom: 2,
  },
  customerNoteText: { fontSize: FontSize.sm, color: Colors.text, lineHeight: 18 },
});
