import { View, Text, StyleSheet, ScrollView, Alert, Pressable, Switch } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Button } from '@/components/Button';
import { Icon, IconName } from '@/components/Icon';
import { SkeletonShopCard } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, LetterSpacing, Radius, Spacing, Layout, monogramFor } from '@/lib/theme';
import { apiClient } from '@/api/client';
import { BarberApi, Stylist } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { useAuthStore } from '@/store/auth-store';
import { salonGenderLabel } from '@/lib/format';
import { Haptics } from '@/lib/haptics';

export default function BarberProfileScreen() {
  const router = useRouter();
  const qc = useQueryClient();
  const signOut = useAuthStore((s) => s.signOut);
  const user = useAuthStore((s) => s.user);

  const shopQuery = useQuery({
    queryKey: ['my-shop'],
    queryFn: async () => {
      const res = await apiClient.get('/v1/owner/barber-shop');
      return res.data;
    },
  });

  const stylistsQuery = useQuery({
    queryKey: ['my-stylists', shopQuery.data?.id],
    queryFn: () => BarberApi.getStylists(shopQuery.data!.id),
    enabled: !!shopQuery.data?.id,
  });

  const toggleWalkInMut = useMutation({
    mutationFn: async (args: { stylistId: string; available: boolean }) => {
      await BarberApi.toggleStylistWalkIn(args.stylistId, args.available);
    },
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-stylists', shopQuery.data?.id] });
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  async function handleLogout() {
    Alert.alert('Çıkış yap', 'Çıkış yapmak istediğine emin misin?', [
      { text: 'Vazgeç', style: 'cancel' },
      {
        text: 'Çıkış',
        style: 'destructive',
        onPress: async () => {
          await signOut();
          router.replace('/(auth)/login');
        },
      },
    ]);
  }

  const shop = shopQuery.data;
  const mono = monogramFor(user?.displayName || 'Kuaför');
  const statusInfo = shop ? statusBadge(shop.status) : null;

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView
        contentContainerStyle={styles.scroll}
        showsVerticalScrollIndicator={false}
      >
        <View style={styles.header}>
          <View style={[styles.avatar, { backgroundColor: mono.bg }]}>
            <Text style={[styles.avatarText, { color: mono.fg }]}>{mono.initials}</Text>
          </View>
          <Text style={styles.name}>{user?.displayName ?? 'Kuaför'}</Text>
          <Text style={styles.phone}>{user?.phone ?? '—'}</Text>
          <View style={{ marginTop: Spacing.sm }}>
            <Badge label="Kuaför Sahibi" tone="info" />
          </View>
        </View>

        <Text style={styles.sectionTitle}>Salonum</Text>
        {shop ? (
          <Card style={{ gap: Spacing.sm }}>
            <View style={styles.shopHeader}>
              <View style={{ flex: 1 }}>
                <Text style={styles.shopName}>{shop.name}</Text>
                {shop.addressLine1 ? (
                  <View style={styles.shopAddressRow}>
                    <Icon name="mapPin" size={12} color={Colors.textMuted} />
                    <Text style={styles.shopAddress} numberOfLines={2}>{shop.addressLine1}</Text>
                  </View>
                ) : null}
              </View>
              {statusInfo ? <Badge label={statusInfo.label} tone={statusInfo.tone as any} /> : null}
            </View>
            <View style={styles.metaRow}>
              <Badge label={salonGenderLabel(shop.genderType)} tone="brand" />
              {shop.phone ? <Badge label={shop.phone} tone="neutral" /> : null}
            </View>
            {shop.description ? (
              <Text style={styles.shopDesc} numberOfLines={3}>{shop.description}</Text>
            ) : null}
          </Card>
        ) : shopQuery.isLoading ? (
          // G15 fix: "Salonum" kartı düz "Yükleniyor…" yerine gerçek karta yakın
          // şekilli bir iskelet gösteriyor (isim + adres + rozet satırı).
          <SkeletonShopCard />
        ) : shopQuery.isError ? (
          // G12 fix: eskiden isError de "Henüz salonun yok" dalına düşüyordu — bir ağ
          // hatasında sahibine "salonun yok, yakında oluşturulacak" YALANI söyleniyordu.
          <Card>
            <Text style={styles.emptyTitle}>Bağlantı kurulamadı</Text>
            <Text style={styles.emptyDesc}>
              Salon bilgine ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene.
            </Text>
            <View style={{ marginTop: Spacing.md }}>
              <Button label="Tekrar dene" onPress={() => shopQuery.refetch()} />
            </View>
          </Card>
        ) : (
          <Card>
            <Text style={styles.emptyTitle}>Henüz salonun yok</Text>
            <Text style={styles.emptyDesc}>
              Salon bilgilerini girip KYC'yi tamamlayınca müşteriler seni bulabilir.
            </Text>
            <View style={{ marginTop: Spacing.md }}>
              <Button label="Salon oluştur" disabled />
              <Text style={styles.smallHint}>Yakında — şu an web admin'den oluşturuluyor.</Text>
            </View>
          </Card>
        )}

        {stylistsQuery.data && stylistsQuery.data.length > 0 ? (
          <>
            <Text style={styles.sectionTitle}>Şu an müsait modu</Text>
            <Card>
              <View style={styles.walkInHintRow}>
                <Icon name="info" size={13} color={Colors.textSecondary} />
                <Text style={styles.walkInHint}>
                  Müşteri keşfette "Şu an boş" filtresi açtığında stilistini görür.
                  Açılan toggle 2 saat sonra otomatik kapanır (sahte vaatleri önler).
                </Text>
              </View>
              {stylistsQuery.data.filter((s) => s.isActive).map((s, idx) => (
                <View key={s.id} style={[styles.walkInRow, idx > 0 && styles.walkInRowBorder]}>
                  <View style={{ flex: 1 }}>
                    <Text style={styles.walkInName}>{s.displayName}</Text>
                    <View style={styles.walkInStatusRow}>
                      <View style={[styles.walkInDot, s.walkInAvailable && styles.walkInDotActive]} />
                      <Text style={styles.walkInStatus}>
                        {s.walkInAvailable ? 'Şu an müsait' : 'Pasif'}
                      </Text>
                    </View>
                  </View>
                  <Switch
                    value={s.walkInAvailable}
                    onValueChange={(value) => toggleWalkInMut.mutate({ stylistId: s.id, available: value })}
                    trackColor={{ true: Colors.success, false: Colors.border }}
                    accessibilityLabel={`${s.displayName} şu an müsait toggle`}
                  />
                </View>
              ))}
            </Card>
          </>
        ) : null}

        <Text style={styles.sectionTitle}>Yönetim</Text>
        <Card padded={false}>
          <ListRow icon="document" label="KYC Doğrulama" hint="Belge gönder" onPress={() => router.push('/(barber)/kyc')} />
          <Divider />
          <ListRow icon="clock" label="Müsaitlik" hint="Saat & izin" onPress={() => router.push('/(barber)/availability')} />
          <Divider />
          <ListRow icon="users" label="Stilistler" hint="Düzenle" onPress={() => router.push('/(barber)/availability')} />
          <Divider />
          <ListRow icon="scissors" label="Hizmetler" hint="CRUD" onPress={() => router.push('/(barber)/services')} />
          <Divider />
          <ListRow icon="creditCard" label="Randevu Ayarları" hint="Kapora & limitler" onPress={() => router.push('/(barber)/booking-settings')} />
          <Divider />
          <ListRow icon="document" label="Yorumlar" hint="Görüntüle & cevapla" onPress={() => router.push('/(barber)/reviews')} />
        </Card>

        <Text style={styles.sectionTitle}>Hesap</Text>
        <Card padded={false}>
          <ListRow icon="bell" label="Bildirimler" hint="Yakında" />
          <Divider />
          <ListRow icon="document" label="Destek" hint="Yakında" />
          <Divider />
          <ListRow icon="info" label="Sürüm" value="0.0.1" />
        </Card>

        <View style={styles.actions}>
          <Button label="Çıkış yap" variant="secondary" onPress={handleLogout} />
        </View>

        <Text style={styles.footer}>Kuaforum Pro • Türkiye'nin kuaför pazaryeri</Text>
      </ScrollView>
    </SafeAreaView>
  );
}

function ListRow({ icon, label, value, hint, onPress }: { icon: IconName; label: string; value?: string; hint?: string; onPress?: () => void }) {
  const content = (
    <View style={styles.listRow}>
      <View style={styles.listIconWrap}>
        <Icon name={icon} size={17} color={Colors.textSecondary} />
      </View>
      <Text style={styles.listLabel}>{label}</Text>
      <View style={{ flex: 1 }} />
      {value ? <Text style={styles.listValue}>{value}</Text> : null}
      {hint ? <Text style={styles.listHint}>{hint}</Text> : null}
      {onPress ? <Icon name="chevronRight" size={16} color={Colors.textMuted} /> : null}
    </View>
  );
  if (onPress) {
    return (
      <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={label}>
        {content}
      </Pressable>
    );
  }
  return content;
}

function Divider() { return <View style={styles.divider} />; }

function statusBadge(s: number): { label: string; tone: string } {
  switch (s) {
    case 0: return { label: 'Taslak', tone: 'neutral' };
    case 1: return { label: 'KYC Bekliyor', tone: 'warning' };
    case 2: return { label: 'Aktif', tone: 'success' };
    case 3: return { label: 'Askıya Alındı', tone: 'error' };
    case 4: return { label: 'Kapalı', tone: 'neutral' };
    default: return { label: '—', tone: 'neutral' };
  }
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xxl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  header: { alignItems: 'center', paddingVertical: Spacing.xl },
  avatar: {
    width: 88,
    height: 88,
    borderRadius: Radius.full,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: Spacing.md,
  },
  avatarText: { fontSize: 32, fontWeight: FontWeight.bold },
  name: { fontSize: FontSize.xl, fontWeight: FontWeight.semibold, color: Colors.text },
  phone: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 4 },
  sectionTitle: {
    fontSize: FontSize.xs,
    color: Colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: LetterSpacing.label,
    marginTop: Spacing.lg,
    marginBottom: Spacing.sm,
    paddingHorizontal: Spacing.xs,
    fontWeight: FontWeight.semibold,
  },
  shopHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: Spacing.sm },
  shopName: { fontSize: FontSize.lg, fontWeight: FontWeight.semibold, color: Colors.text },
  shopAddressRow: { flexDirection: 'row', alignItems: 'center', gap: 4, marginTop: 4 },
  shopAddress: { flex: 1, fontSize: FontSize.xs, color: Colors.textMuted },
  metaRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  shopDesc: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 20 },
  muted: { fontSize: FontSize.sm, color: Colors.textMuted },
  emptyTitle: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  emptyDesc: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 4, lineHeight: 20 },
  smallHint: { fontSize: FontSize.xs, color: Colors.textMuted, textAlign: 'center', marginTop: 6 },

  walkInHintRow: { flexDirection: 'row', alignItems: 'flex-start', gap: 6, marginBottom: Spacing.sm },
  walkInHint: {
    flex: 1,
    fontSize: FontSize.xs,
    color: Colors.textSecondary,
    lineHeight: 16,
  },
  walkInRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: Spacing.sm,
    gap: Spacing.md,
  },
  walkInRowBorder: {
    borderTopWidth: 1,
    borderTopColor: Colors.divider,
  },
  walkInName: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  walkInStatusRow: { flexDirection: 'row', alignItems: 'center', gap: 6, marginTop: 2 },
  walkInDot: { width: 7, height: 7, borderRadius: 4, backgroundColor: Colors.borderStrong },
  walkInDotActive: { backgroundColor: Colors.success },
  walkInStatus: { fontSize: FontSize.xs, color: Colors.textMuted },
  listRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: 14,
    paddingHorizontal: Spacing.lg,
    gap: Spacing.md,
    minHeight: 52,
  },
  listIconWrap: { width: 24, alignItems: 'center' },
  listLabel: { fontSize: FontSize.md, color: Colors.text, fontWeight: FontWeight.medium },
  listValue: { fontSize: FontSize.sm, color: Colors.textSecondary },
  listHint: { fontSize: FontSize.xs, color: Colors.textMuted },
  divider: { height: 1, backgroundColor: Colors.divider, marginLeft: 56 },
  actions: { gap: Spacing.sm, marginTop: Spacing.xl },
  footer: { fontSize: FontSize.xs, color: Colors.textMuted, textAlign: 'center', marginTop: Spacing.xl },
});
