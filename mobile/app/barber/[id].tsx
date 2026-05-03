import { useState } from 'react';
import { View, Text, StyleSheet, ScrollView, Pressable, Alert, Linking, Platform } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { EmptyState } from '@/components/EmptyState';
import { Colors, FontSize, Radius, Spacing } from '@/lib/theme';
import { BarberApi } from '@/api/barber';
import { ReviewApi } from '@/api/review';
import { formatDuration, formatTime, formatTry } from '@/lib/format';

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

  function openDirections() {
    const shop = shopQuery.data;
    if (!shop?.latitude || !shop?.longitude) {
      Alert.alert('Konum yok', 'Bu kuaförün konumu tanımlı değil.');
      return;
    }
    // FREE deep link — no API call, no key required
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

  const today = new Date().toISOString().slice(0, 10);
  const week = new Date(Date.now() + 7 * 86400_000).toISOString().slice(0, 10);
  const slotsQuery = useQuery({
    queryKey: ['availability', selectedStylist, Array.from(selectedServices).sort().join(',')],
    queryFn: () =>
      BarberApi.computeAvailability(selectedStylist!, today, week, Array.from(selectedServices)),
    enabled: !!selectedStylist && selectedServices.size > 0,
  });

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
    if (!selectedStylist) {
      Alert.alert('Stilist seç');
      return;
    }
    if (selectedServices.size === 0) {
      Alert.alert('Hizmet seç', 'En az bir hizmet seçmelisin.');
      return;
    }
    if (!selectedSlot) {
      Alert.alert('Saat seç');
      return;
    }
    router.push({
      pathname: '/booking/confirm',
      params: {
        stylistId: selectedStylist,
        scheduledAt: selectedSlot,
        services: Array.from(selectedServices).join(','),
      },
    });
  }

  if (shopQuery.isLoading) return <SafeAreaView style={styles.container}><EmptyState title="Yükleniyor..." /></SafeAreaView>;
  if (shopQuery.isError || !shopQuery.data)
    return (
      <SafeAreaView style={styles.container}>
        <EmptyState title="Bulunamadı" description="Bu kuaför yüklenemedi." />
      </SafeAreaView>
    );

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView>
        <View style={styles.header}>
          <Pressable onPress={() => router.back()}>
            <Text style={styles.back}>← Geri</Text>
          </Pressable>
          <Text style={styles.shopName}>{shopQuery.data.name}</Text>
          {shopQuery.data.description ? <Text style={styles.shopDesc}>{shopQuery.data.description}</Text> : null}
          {shopQuery.data.addressLine1 ? <Text style={styles.shopAddr}>📍 {shopQuery.data.addressLine1}</Text> : null}
          {reviewsQuery.data && reviewsQuery.data.summary.count > 0 ? (
            <Text style={styles.ratingLine}>
              ⭐ {reviewsQuery.data.summary.average.toFixed(1)} ({reviewsQuery.data.summary.count} yorum)
            </Text>
          ) : null}
          {shopQuery.data.latitude && shopQuery.data.longitude ? (
            <Pressable onPress={openDirections} style={styles.directionsBtn}>
              <Text style={styles.directionsText}>🗺  Yol tarifi al</Text>
            </Pressable>
          ) : null}
        </View>

        <Section title="Hizmetler">
          {(servicesQuery.data ?? []).map((s) => {
            const selected = selectedServices.has(s.id);
            return (
              <Pressable key={s.id} onPress={() => toggleService(s.id)}>
                <Card style={[styles.serviceCard, selected && styles.serviceCardSelected]}>
                  <View style={styles.flexBetween}>
                    <View style={{ flex: 1 }}>
                      <Text style={styles.serviceName}>{s.name}</Text>
                      <Text style={styles.serviceDuration}>{formatDuration(s.durationMinutes)}</Text>
                    </View>
                    <Text style={styles.servicePrice}>{formatTry(s.priceMinor)}</Text>
                  </View>
                </Card>
              </Pressable>
            );
          })}
        </Section>

        <Section title="Stilist seç">
          <View style={styles.stylistRow}>
            {(stylistsQuery.data ?? []).map((st) => (
              <Pressable key={st.id} onPress={() => setSelectedStylist(st.id)} style={[styles.stylistPill, selectedStylist === st.id && styles.stylistPillSelected]}>
                <Text style={[styles.stylistText, selectedStylist === st.id && styles.stylistTextSelected]}>
                  {st.displayName}
                </Text>
              </Pressable>
            ))}
          </View>
        </Section>

        {reviewsQuery.data && reviewsQuery.data.items.length > 0 ? (
          <Section title={`Yorumlar (${reviewsQuery.data.summary.count})`}>
            {reviewsQuery.data.items.map((r) => (
              <Card key={r.id} style={styles.reviewCard}>
                <View style={styles.reviewHead}>
                  <Text style={styles.reviewStars}>{'★'.repeat(r.rating)}{'☆'.repeat(5 - r.rating)}</Text>
                  <Text style={styles.reviewDate}>
                    {new Date(r.createdAt).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })}
                  </Text>
                </View>
                {r.body ? <Text style={styles.reviewBody}>{r.body}</Text> : null}
                {r.replyBody ? (
                  <View style={styles.replyBox}>
                    <Text style={styles.replyLabel}>Kuaförün cevabı:</Text>
                    <Text style={styles.replyBody}>{r.replyBody}</Text>
                  </View>
                ) : null}
              </Card>
            ))}
          </Section>
        ) : null}

        {selectedStylist && selectedServices.size > 0 ? (
          <Section title="Müsait saatler">
            {slotsQuery.isLoading ? (
              <EmptyState title="Hesaplanıyor..." />
            ) : (slotsQuery.data?.length ?? 0) === 0 ? (
              <EmptyState title="Müsait slot yok" description="Farklı bir stilist veya hizmet kombinasyonu deneyin." />
            ) : (
              <View style={styles.slotGrid}>
                {slotsQuery.data!.slice(0, 24).map((slot) => (
                  <Pressable
                    key={slot.startsAt}
                    onPress={() => setSelectedSlot(slot.startsAt)}
                    style={[styles.slotChip, selectedSlot === slot.startsAt && styles.slotChipSelected]}
                  >
                    <Text style={[styles.slotText, selectedSlot === slot.startsAt && styles.slotTextSelected]}>
                      {formatTime(slot.startsAt)}
                    </Text>
                  </Pressable>
                ))}
              </View>
            )}
          </Section>
        ) : null}
      </ScrollView>

      {selectedServices.size > 0 ? (
        <View style={styles.footerBar}>
          <View>
            <Text style={styles.footerTotal}>{formatTry(totalAmount)}</Text>
            <Text style={styles.footerDuration}>{formatDuration(totalDuration)}</Text>
          </View>
          <Button
            label="Devam et"
            onPress={handleContinue}
            disabled={!selectedSlot || !selectedStylist}
            style={{ flex: 1, marginLeft: Spacing.md }}
          />
        </View>
      ) : null}
    </SafeAreaView>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <View style={styles.section}>
      <Text style={styles.sectionTitle}>{title}</Text>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: { padding: Spacing.lg },
  back: { color: Colors.primary, fontSize: FontSize.sm, marginBottom: Spacing.sm },
  shopName: { fontSize: FontSize.xxl, fontWeight: '700', color: Colors.text },
  shopDesc: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: Spacing.xs },
  shopAddr: { fontSize: FontSize.sm, color: Colors.textMuted, marginTop: Spacing.sm },
  ratingLine: { fontSize: FontSize.sm, color: Colors.text, marginTop: Spacing.xs, fontWeight: '600' },
  directionsBtn: {
    marginTop: Spacing.md,
    paddingVertical: Spacing.sm,
    paddingHorizontal: Spacing.md,
    borderRadius: Radius.md,
    backgroundColor: 'rgba(107, 79, 79, 0.1)',
    alignSelf: 'flex-start',
  },
  directionsText: { fontSize: FontSize.sm, color: Colors.primary, fontWeight: '600' },
  reviewCard: { gap: Spacing.xs },
  reviewHead: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  reviewStars: { fontSize: FontSize.md, color: Colors.accent, letterSpacing: 2 },
  reviewDate: { fontSize: FontSize.xs, color: Colors.textMuted },
  reviewBody: { fontSize: FontSize.sm, color: Colors.text, lineHeight: 20 },
  replyBox: {
    marginTop: Spacing.sm,
    padding: Spacing.sm,
    backgroundColor: Colors.background,
    borderRadius: Radius.sm,
    borderLeftWidth: 3,
    borderLeftColor: Colors.primary,
  },
  replyLabel: { fontSize: FontSize.xs, color: Colors.primary, fontWeight: '700', marginBottom: 2 },
  replyBody: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 18 },
  section: { padding: Spacing.lg, paddingTop: 0, gap: Spacing.sm },
  sectionTitle: { fontSize: FontSize.lg, fontWeight: '600', color: Colors.text, marginBottom: Spacing.sm },
  serviceCard: { borderColor: Colors.border },
  serviceCardSelected: { borderColor: Colors.primary, borderWidth: 2 },
  flexBetween: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  serviceName: { fontSize: FontSize.md, color: Colors.text, fontWeight: '500' },
  serviceDuration: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },
  servicePrice: { fontSize: FontSize.md, color: Colors.primary, fontWeight: '700' },
  stylistRow: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.sm },
  stylistPill: {
    paddingHorizontal: Spacing.md,
    paddingVertical: Spacing.sm,
    borderRadius: Radius.full,
    backgroundColor: Colors.surface,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  stylistPillSelected: { backgroundColor: Colors.primary, borderColor: Colors.primary },
  stylistText: { fontSize: FontSize.sm, color: Colors.text },
  stylistTextSelected: { color: '#fff', fontWeight: '600' },
  slotGrid: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.sm },
  slotChip: {
    paddingHorizontal: Spacing.md,
    paddingVertical: Spacing.sm,
    borderRadius: Radius.sm,
    backgroundColor: Colors.surface,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  slotChipSelected: { backgroundColor: Colors.primary, borderColor: Colors.primary },
  slotText: { fontSize: FontSize.sm, color: Colors.text },
  slotTextSelected: { color: '#fff', fontWeight: '600' },
  footerBar: {
    flexDirection: 'row',
    alignItems: 'center',
    padding: Spacing.lg,
    backgroundColor: Colors.surface,
    borderTopWidth: 1,
    borderTopColor: Colors.border,
  },
  footerTotal: { fontSize: FontSize.lg, fontWeight: '700', color: Colors.primary },
  footerDuration: { fontSize: FontSize.xs, color: Colors.textMuted },
});
