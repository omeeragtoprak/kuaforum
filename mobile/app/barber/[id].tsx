import { useMemo, useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Pressable, Alert, Linking, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Chip } from '@/components/Chip';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { ErrorState } from '@/components/ErrorState';
import { Icon } from '@/components/Icon';
import { SkeletonBarberDetail } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, LetterSpacing, Radius, Spacing, Layout, Shadow, monogramFor } from '@/lib/theme';
import { BarberApi } from '@/api/barber';
import { ReviewApi } from '@/api/review';
import { formatDuration, formatTime, formatTry, salonGenderLabel } from '@/lib/format';

export default function BarberDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const [selectedServices, setSelectedServices] = useState<Set<string>>(new Set());
  const [selectedStylist, setSelectedStylist] = useState<string | null>(null);
  const [selectedSlot, setSelectedSlot] = useState<string | null>(null);

  const shopQuery = useQuery({ queryKey: ['barber', id], queryFn: () => BarberApi.getById(id!) });
  const stylistsQuery = useQuery({ queryKey: ['stylists', id], queryFn: () => BarberApi.getStylists(id!) });
  const servicesQuery = useQuery({ queryKey: ['services', id], queryFn: () => BarberApi.getServices(id!) });
  const reviewsQuery = useQuery({
    queryKey: ['shop-reviews', id],
    queryFn: () => ReviewApi.getShopReviews(id!, 1, 5),
  });

  const today = new Date().toISOString().slice(0, 10);
  const week = new Date(Date.now() + 7 * 86400_000).toISOString().slice(0, 10);
  const slotsQuery = useQuery({
    queryKey: ['availability', selectedStylist, Array.from(selectedServices).sort().join(',')],
    queryFn: () =>
      BarberApi.computeAvailability(selectedStylist!, today, week, Array.from(selectedServices)),
    enabled: !!selectedStylist && selectedServices.size > 0,
  });

  const slotsByDay = useMemo(() => {
    const groups: { dateKey: string; label: string; slots: typeof slotsQuery.data }[] = [];
    if (!slotsQuery.data) return groups;
    const todayKey = new Date();
    todayKey.setHours(0, 0, 0, 0);
    const tomorrowKey = new Date(todayKey);
    tomorrowKey.setDate(todayKey.getDate() + 1);
    for (const slot of slotsQuery.data) {
      const d = new Date(slot.startsAt);
      const dateKey = d.toISOString().slice(0, 10);
      let group = groups.find((g) => g.dateKey === dateKey);
      if (!group) {
        const dKey = new Date(d);
        dKey.setHours(0, 0, 0, 0);
        let label: string;
        if (dKey.getTime() === todayKey.getTime()) label = 'Bugün';
        else if (dKey.getTime() === tomorrowKey.getTime()) label = 'Yarın';
        else label = d.toLocaleDateString('tr-TR', { weekday: 'long', day: 'numeric', month: 'long' });
        group = { dateKey, label, slots: [] };
        groups.push(group);
      }
      group.slots!.push(slot);
    }
    return groups;
  }, [slotsQuery.data]);

  function openDirections() {
    const shop = shopQuery.data;
    if (!shop?.latitude || !shop?.longitude) {
      Alert.alert('Konum yok', 'Bu kuaförün konumu tanımlı değil.');
      return;
    }
    const lat = shop.latitude;
    const lng = shop.longitude;
    const label = encodeURIComponent(shop.name);
    const url = Platform.select({
      ios: `https://maps.apple.com/?daddr=${lat},${lng}&q=${label}`,
      android: `geo:${lat},${lng}?q=${lat},${lng}(${label})`,
      default: `https://www.google.com/maps/dir/?api=1&destination=${lat},${lng}`,
    })!;
    Linking.openURL(url).catch(() => {
      Linking.openURL(`https://www.google.com/maps/dir/?api=1&destination=${lat},${lng}`);
    });
  }

  function toggleService(serviceId: string) {
    const next = new Set(selectedServices);
    if (next.has(serviceId)) next.delete(serviceId);
    else next.add(serviceId);
    setSelectedServices(next);
    setSelectedSlot(null);
  }

  const totalAmount = (servicesQuery.data ?? [])
    .filter((s) => selectedServices.has(s.id))
    .reduce((sum, s) => sum + s.priceMinor, 0);

  const totalDuration = (servicesQuery.data ?? [])
    .filter((s) => selectedServices.has(s.id))
    .reduce((sum, s) => sum + s.durationMinutes + s.bufferMinutes, 0);

  function handleContinue() {
    if (!selectedStylist) return Alert.alert('Stilist seç', 'Devam etmek için bir stilist seç.');
    if (selectedServices.size === 0) return Alert.alert('Hizmet seç', 'En az bir hizmet seçmelisin.');
    if (!selectedSlot) return Alert.alert('Saat seç', 'Müsait saatlerden birini seç.');
    const stylistName =
      (stylistsQuery.data ?? []).find((s) => s.id === selectedStylist)?.displayName ?? '';
    const serviceLabels = (servicesQuery.data ?? [])
      .filter((s) => selectedServices.has(s.id))
      .map((s) => s.name)
      .join(' · ');
    // RFC-014 §2 D4: confirm.tsx'in ön-kontrol (check-conflict) çağırabilmesi için seçili
    // slotun bitişi lazım — uygunluk sorgusundan dönen SlotDto{startsAt,endsAt} zaten elimizde,
    // süreyi hizmetlerden yeniden hesaplamıyoruz (backend'in kendi hesabıyla tutarsızlık riski).
    const selectedSlotEndsAt = (slotsQuery.data ?? []).find((s) => s.startsAt === selectedSlot)?.endsAt;
    router.push({
      pathname: '/booking/confirm',
      params: {
        stylistId: selectedStylist,
        stylistName,
        scheduledAt: selectedSlot,
        scheduledEndsAt: selectedSlotEndsAt,
        services: Array.from(selectedServices).join(','),
        serviceLabels,
        totalAmount: String(totalAmount),
        totalDuration: String(totalDuration),
        shopName: shop.name,
        barberShopId: shop.id,
        // RFC-014 §2 D2 madde 6: confirm.tsx randevu öncesi doğru vaadi göstermek için buna
        // ihtiyaç duyuyor (shop verisini kendi başına fetch etmiyor, tüm seçimler param'la taşınıyor).
        autoConfirmEnabled: String(shop.autoConfirmEnabled),
      },
    });
  }

  if (shopQuery.isLoading) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <SkeletonBarberDetail />
      </SafeAreaView>
    );
  }
  if (shopQuery.isError || !shopQuery.data) {
    // G12 fix: 404 (kuaför gerçekten silinmiş/yayından kaldırılmış) ile geçici
    // sunucu/ağ hatası artık aynı "Bulunamadı" metnine düşmüyor — bkz. ErrorState.
    return (
      <SafeAreaView style={styles.container}>
        <ErrorState
          error={shopQuery.error}
          onRetry={() => shopQuery.refetch()}
          notFoundIcon="x"
          notFoundTitle="Bu kuaför artık yayında değil"
          notFoundDescription="Bu salon kaldırılmış veya profili yayından kaldırılmış olabilir."
          notFoundActionLabel="Geri dön"
          onNotFoundAction={() => router.back()}
        />
      </SafeAreaView>
    );
  }

  const shop = shopQuery.data;
  const summary = reviewsQuery.data?.summary;
  const mono = monogramFor(shop.name);

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView
        contentContainerStyle={{ paddingBottom: 140 }}
        showsVerticalScrollIndicator={false}
      >
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

          <View style={[styles.heroBubble, { backgroundColor: mono.bg }]}>
            <Text style={[styles.heroInitials, { color: mono.fg }]}>{mono.initials}</Text>
          </View>
          <Text style={styles.shopName}>{shop.name}</Text>
          {shop.description ? (
            <Text style={styles.shopDesc}>{shop.description}</Text>
          ) : null}

          <View style={styles.metaRow}>
            <Badge label={salonGenderLabel(shop.genderType)} tone="brand" />
            {summary && summary.count > 0 ? (
              <View style={styles.ratingInline}>
                <Icon name="star" size={13} color={Colors.star} />
                <Text style={styles.ratingInlineValue}>{summary.average.toFixed(1)}</Text>
                <Text style={styles.ratingInlineCount}>({summary.count})</Text>
              </View>
            ) : null}
          </View>

          {shop.addressLine1 ? (
            <View style={styles.addressRow}>
              <Icon name="mapPin" size={16} color={Colors.textSecondary} />
              <Text style={styles.addressText} numberOfLines={2}>{shop.addressLine1}</Text>
              {shop.latitude && shop.longitude ? (
                <Pressable onPress={openDirections} style={styles.directionsBtn} hitSlop={6}>
                  <Text style={styles.directionsText}>Yol tarifi</Text>
                </Pressable>
              ) : null}
            </View>
          ) : null}
        </View>

        <Section title="Hizmetler" subtitle="Birden fazla seçebilirsin">
          {(servicesQuery.data ?? []).map((s) => {
            const selected = selectedServices.has(s.id);
            return (
              <Pressable
                key={s.id}
                onPress={() => toggleService(s.id)}
                style={[styles.serviceCard, selected && styles.serviceCardSelected]}
                accessibilityRole="checkbox"
                accessibilityState={{ checked: selected }}
                accessibilityLabel={`${s.name}, ${formatTry(s.priceMinor)}`}
              >
                <View style={[styles.checkbox, selected && styles.checkboxSelected]}>
                  {selected ? <Icon name="check" size={13} color={Colors.textInverse} strokeWidth={2.4} /> : null}
                </View>
                <View style={{ flex: 1 }}>
                  <Text style={styles.serviceName}>{s.name}</Text>
                  <Text style={styles.serviceMeta}>{formatDuration(s.durationMinutes + s.bufferMinutes)}</Text>
                </View>
                <Text style={styles.servicePrice}>{formatTry(s.priceMinor)}</Text>
              </Pressable>
            );
          })}
        </Section>

        <Section title="Stilist" subtitle="Çalışmak istediğin uzmanı seç">
          <View style={styles.chipsRow}>
            {(stylistsQuery.data ?? []).map((st) => (
              <Chip
                key={st.id}
                label={st.displayName}
                selected={selectedStylist === st.id}
                onPress={() => setSelectedStylist(st.id)}
              />
            ))}
          </View>
        </Section>

        {selectedStylist && selectedServices.size > 0 ? (
          <Section title="Müsait saatler" subtitle="Önümüzdeki 7 gün">
            {slotsQuery.isLoading ? (
              <Text style={styles.muted}>Hesaplanıyor…</Text>
            ) : slotsQuery.isError ? (
              <EmptyState
                icon="alert"
                title="Slotlar alınamadı"
                description="Bağlantı hatası — tekrar dene."
                actionLabel="Yenile"
                onAction={() => slotsQuery.refetch()}
              />
            ) : (slotsQuery.data?.length ?? 0) === 0 ? (
              <EmptyState
                icon="calendar"
                title="Müsait slot yok"
                description="Farklı bir stilist veya hizmet seçimi dene."
              />
            ) : (
              <View style={{ gap: Spacing.md }}>
                {slotsByDay.map((group) => (
                  <View key={group.dateKey} style={{ gap: Spacing.sm }}>
                    <Text style={styles.dayHeader}>{group.label}</Text>
                    <View style={styles.slotGrid}>
                      {group.slots!.map((slot) => (
                        <Pressable
                          key={slot.startsAt}
                          onPress={() => setSelectedSlot(slot.startsAt)}
                          style={[
                            styles.slotChip,
                            selectedSlot === slot.startsAt && styles.slotChipSelected,
                          ]}
                          accessibilityRole="radio"
                          accessibilityState={{ checked: selectedSlot === slot.startsAt }}
                        >
                          <Text style={[
                            styles.slotText,
                            selectedSlot === slot.startsAt && styles.slotTextSelected,
                          ]}>
                            {formatTime(slot.startsAt)}
                          </Text>
                        </Pressable>
                      ))}
                    </View>
                  </View>
                ))}
              </View>
            )}
          </Section>
        ) : null}

        {summary && (reviewsQuery.data?.items?.length ?? 0) > 0 ? (
          <Section title={`Yorumlar (${summary.count})`} subtitle={`Ortalama ${summary.average.toFixed(1)} ★`}>
            {(reviewsQuery.data?.items ?? []).map((r) => (
              <Card key={r.id} style={{ gap: 6 }}>
                <View style={styles.reviewHead}>
                  <Text style={styles.reviewStars}>{'★'.repeat(r.rating)}{'☆'.repeat(5 - r.rating)}</Text>
                  <Text style={styles.reviewDate}>
                    {new Date(r.createdAt).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })}
                  </Text>
                </View>
                {r.body ? <Text style={styles.reviewBody}>{r.body}</Text> : null}
                {r.replyBody ? (
                  <View style={styles.replyBox}>
                    <Text style={styles.replyLabel}>Kuaförden:</Text>
                    <Text style={styles.reviewBody}>{r.replyBody}</Text>
                  </View>
                ) : null}
              </Card>
            ))}
          </Section>
        ) : null}
      </ScrollView>

      {selectedServices.size > 0 ? (
        <View style={styles.footerBar}>
          <View style={{ flex: 1 }}>
            <Text style={styles.footerTotalLabel}>Toplam</Text>
            <Text style={styles.footerTotal}>{formatTry(totalAmount)}</Text>
            <Text style={styles.footerDuration}>{formatDuration(totalDuration)}</Text>
          </View>
          <Button
            label="Devam et"
            onPress={handleContinue}
            disabled={!selectedSlot || !selectedStylist}
            fullWidth={false}
            size="lg"
            style={{ paddingHorizontal: 32 }}
          />
        </View>
      ) : null}
    </SafeAreaView>
  );
}

function Section({ title, subtitle, children }: { title: string; subtitle?: string; children: React.ReactNode }) {
  return (
    <View style={styles.section}>
      <Text style={styles.sectionTitle}>{title}</Text>
      {subtitle ? <Text style={styles.sectionSub}>{subtitle}</Text> : null}
      <View style={{ gap: Spacing.sm }}>{children}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.sm,
    paddingBottom: Spacing.lg,
    alignItems: 'flex-start',
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  backBtn: { flexDirection: 'row', alignItems: 'center', gap: 2, paddingVertical: 6, marginLeft: -4 },
  back: { color: Colors.ink, fontSize: FontSize.sm, fontWeight: FontWeight.medium },
  heroBubble: {
    width: 72,
    height: 72,
    borderRadius: Radius.full,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: Spacing.md,
  },
  heroInitials: { fontSize: FontSize.xl, fontWeight: FontWeight.bold },
  shopName: {
    fontSize: FontSize.xxl,
    fontWeight: FontWeight.bold,
    color: Colors.text,
    marginTop: Spacing.md,
    letterSpacing: LetterSpacing.title,
  },
  shopDesc: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 6, lineHeight: 20 },
  metaRow: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: 10, marginTop: Spacing.md },
  ratingInline: { flexDirection: 'row', alignItems: 'center', gap: 4 },
  ratingInlineValue: { fontSize: FontSize.sm, color: Colors.text, fontWeight: FontWeight.bold },
  ratingInlineCount: { fontSize: FontSize.xs, color: Colors.textMuted },
  addressRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginTop: Spacing.md,
    backgroundColor: Colors.surfaceAlt,
    paddingHorizontal: Spacing.md,
    paddingVertical: 10,
    borderRadius: Radius.md,
    alignSelf: 'stretch',
  },
  addressText: { flex: 1, fontSize: FontSize.sm, color: Colors.textSecondary },
  directionsBtn: {
    backgroundColor: Colors.accentSoft,
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: Radius.sm,
  },
  directionsText: { fontSize: FontSize.xs, color: Colors.accentInk, fontWeight: FontWeight.semibold },
  section: {
    paddingHorizontal: Layout.screenPaddingX,
    marginTop: Spacing.lg,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  sectionTitle: { fontSize: FontSize.lg, fontWeight: FontWeight.semibold, color: Colors.text },
  sectionSub: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2, marginBottom: Spacing.md },
  muted: { fontSize: FontSize.sm, color: Colors.textMuted },
  serviceCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
    backgroundColor: Colors.surface,
    borderRadius: Radius.md,
    padding: Spacing.md,
    borderWidth: 1.5,
    borderColor: Colors.border,
  },
  serviceCardSelected: { borderColor: Colors.ink, backgroundColor: Colors.surfaceAlt },
  checkbox: {
    width: 24, height: 24, borderRadius: 12,
    borderWidth: 2, borderColor: Colors.borderStrong,
    alignItems: 'center', justifyContent: 'center',
  },
  checkboxSelected: { backgroundColor: Colors.ink, borderColor: Colors.ink },
  serviceName: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.medium },
  serviceMeta: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  servicePrice: { fontSize: FontSize.md, color: Colors.ink, fontWeight: FontWeight.bold },
  chipsRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  slotGrid: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  dayHeader: {
    fontSize: FontSize.sm,
    fontWeight: FontWeight.bold,
    color: Colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: LetterSpacing.label,
  },
  slotChip: {
    paddingHorizontal: 14,
    paddingVertical: 10,
    borderRadius: Radius.sm,
    backgroundColor: Colors.surface,
    borderWidth: 1.5,
    borderColor: Colors.border,
    minWidth: 76,
    alignItems: 'center',
  },
  slotChipSelected: { backgroundColor: Colors.ink, borderColor: Colors.ink },
  slotText: { fontSize: FontSize.sm, color: Colors.text, fontWeight: FontWeight.semibold },
  slotTextSelected: { color: Colors.textInverse },
  reviewHead: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  reviewStars: { fontSize: FontSize.md, color: Colors.star, letterSpacing: 2 },
  reviewDate: { fontSize: FontSize.xs, color: Colors.textMuted },
  reviewBody: { fontSize: FontSize.sm, color: Colors.text, lineHeight: 20 },
  replyBox: {
    marginTop: Spacing.sm,
    padding: 10,
    backgroundColor: Colors.surfaceAlt,
    borderRadius: Radius.sm,
    borderLeftWidth: 3,
    borderLeftColor: Colors.ink,
  },
  replyLabel: { fontSize: FontSize.xs, color: Colors.ink, fontWeight: FontWeight.bold, marginBottom: 2 },
  footerBar: {
    position: 'absolute',
    left: 0,
    right: 0,
    bottom: 0,
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: Spacing.lg,
    paddingTop: Spacing.md,
    paddingBottom: Spacing.lg,
    backgroundColor: Colors.surface,
    borderTopWidth: 1,
    borderTopColor: Colors.border,
    gap: Spacing.md,
    ...Shadow.sm,
  },
  footerTotalLabel: { fontSize: 11, color: Colors.textMuted, textTransform: 'uppercase', letterSpacing: LetterSpacing.label },
  footerTotal: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.ink },
  footerDuration: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
});
