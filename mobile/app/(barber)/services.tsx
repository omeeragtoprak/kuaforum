import { useState } from 'react';
import {
  View, Text, StyleSheet, FlatList, Modal, Alert, KeyboardAvoidingView,
  Platform, Pressable, ScrollView,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Badge } from '@/components/Badge';
import { Chip } from '@/components/Chip';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { EmptyState } from '@/components/EmptyState';
import { Icon } from '@/components/Icon';
import { SkeletonShopCard } from '@/components/Skeleton';
import { Colors, FontSize, FontWeight, LetterSpacing, Radius, Spacing, Layout } from '@/lib/theme';
import { BarberApi, type Service } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { formatDuration, formatTry, serviceGenderLabel } from '@/lib/format';
import { Haptics } from '@/lib/haptics';

const GENDER_OPTIONS = [
  { value: 0, label: 'Herkes' },
  { value: 1, label: 'Sadece Kadın' },
  { value: 2, label: 'Sadece Erkek' },
];

const genderText = serviceGenderLabel;

export default function BarberServicesScreen() {
  const qc = useQueryClient();
  const shopQuery = useQuery({ queryKey: ['my-shop'], queryFn: () => BarberApi.getMyShop() });
  const servicesQuery = useQuery({
    queryKey: ['my-shop-services', shopQuery.data?.id],
    queryFn: () => BarberApi.getServices(shopQuery.data!.id),
    enabled: !!shopQuery.data?.id,
  });

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [gender, setGender] = useState(0);
  const [duration, setDuration] = useState('30');
  const [priceTl, setPriceTl] = useState('300');
  const [buffer, setBuffer] = useState('5');
  const [description, setDescription] = useState('');

  const reset = () => {
    setEditingId(null);
    setName(''); setDescription(''); setDuration('30'); setPriceTl('300'); setBuffer('5'); setGender(0);
  };

  function openCreate() {
    reset();
    setModalOpen(true);
  }

  function openEdit(s: Service) {
    setEditingId(s.id);
    setName(s.name);
    setGender(s.gender);
    setDuration(String(s.durationMinutes));
    setBuffer(String(s.bufferMinutes));
    setPriceTl((s.priceMinor / 100).toString());
    setDescription(s.description ?? '');
    setModalOpen(true);
  }

  const createMut = useMutation({
    mutationFn: () => {
      const dur = parseInt(duration, 10);
      const buf = parseInt(buffer, 10);
      const priceMinor = Math.round(parseFloat(priceTl) * 100);
      return BarberApi.createService(shopQuery.data!.id, {
        name: name.trim(),
        gender,
        durationMinutes: dur,
        priceMinor,
        bufferMinutes: buf,
        description: description || undefined,
      });
    },
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-shop-services'] });
      setModalOpen(false);
      reset();
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const updateMut = useMutation({
    mutationFn: () => {
      const dur = parseInt(duration, 10);
      const buf = parseInt(buffer, 10);
      const priceMinor = Math.round(parseFloat(priceTl) * 100);
      return BarberApi.updateService(shopQuery.data!.id, editingId!, {
        name: name.trim(),
        description: description.trim() ? description.trim() : null,
        durationMinutes: dur,
        priceMinor,
        bufferMinutes: buf,
      });
    },
    onSuccess: () => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-shop-services'] });
      setModalOpen(false);
      reset();
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const deleteMut = useMutation({
    mutationFn: (id: string) => BarberApi.deleteService(shopQuery.data!.id, id),
    onSuccess: () => { Haptics.warning(); qc.invalidateQueries({ queryKey: ['my-shop-services'] }); },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  function handleDelete(s: Service) {
    Alert.alert(
      'Hizmeti sil',
      `"${s.name}" hizmetini silmek istediğine emin misin? Mevcut randevular etkilenmez ama yeni rezervasyon alamazsın.`,
      [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'Sil', style: 'destructive', onPress: () => deleteMut.mutate(s.id) },
      ],
    );
  }

  // G12 fix: `getMyShop()` artık ağ/5xx hatasında sessizce null dönmüyor (bkz. src/api/barber.ts),
  // bu yüzden burada `isError` ile "gerçekten salon yok" ayrı dallara ayrılmalı — aksi halde bir
  // WiFi kopukluğunda kuaföre "salonun yok, önce oluştur" YALANI söylenir.
  if (shopQuery.isLoading) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <View style={styles.header}>
          <View style={{ flex: 1 }}>
            <Text style={styles.title}>Hizmetler</Text>
          </View>
        </View>
        <View style={styles.list}>
          <SkeletonShopCard />
        </View>
      </SafeAreaView>
    );
  }

  if (shopQuery.isError) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <EmptyState
          icon="globe"
          title="Bağlantı kurulamadı"
          description="Salon bilgine ulaşılamadı. İnternet bağlantını kontrol edip tekrar dene."
          actionLabel="Tekrar dene"
          onAction={() => shopQuery.refetch()}
        />
      </SafeAreaView>
    );
  }

  if (!shopQuery.data) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <EmptyState
          icon="store"
          title="Salon yok"
          description="Önce Profil sekmesinden salon bilgilerini girmelisin."
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <View style={styles.header}>
        <View style={{ flex: 1 }}>
          <Text style={styles.title}>Hizmetler</Text>
          <Text style={styles.subtitle}>{servicesQuery.data?.length ?? 0} aktif hizmet</Text>
        </View>
        <Button
          label="Yeni"
          icon={<Icon name="plus" size={16} color={Colors.textInverse} strokeWidth={2} />}
          size="sm"
          fullWidth={false}
          onPress={openCreate}
        />
      </View>

      <FlatList
        data={servicesQuery.data ?? []}
        keyExtractor={(s) => s.id}
        contentContainerStyle={styles.list}
        showsVerticalScrollIndicator={false}
        ItemSeparatorComponent={() => <View style={{ height: Spacing.sm }} />}
        ListEmptyComponent={
          servicesQuery.isLoading ? null : servicesQuery.isError ? (
            <EmptyState
              icon="alert"
              title="Bağlantı hatası"
              description="Hizmetler yüklenemedi."
              actionLabel="Tekrar dene"
              onAction={() => servicesQuery.refetch()}
            />
          ) : (
            <EmptyState
              icon="scissors"
              title="Henüz hizmet yok"
              description="Hizmetlerini tanımla — fiyat ve süreyle birlikte."
              actionLabel="İlk hizmetini ekle"
              onAction={openCreate}
            />
          )
        }
        renderItem={({ item }) => (
          <Pressable onPress={() => openEdit(item)} accessibilityRole="button" accessibilityLabel={`${item.name} hizmetini düzenle`}>
            <Card style={{ gap: Spacing.sm }}>
              <View style={styles.serviceHeader}>
                <Text style={styles.serviceName} numberOfLines={1}>{item.name}</Text>
                <Text style={styles.servicePrice}>{formatTry(item.priceMinor)}</Text>
              </View>
              {item.description ? (
                <Text style={styles.serviceDesc} numberOfLines={2}>{item.description}</Text>
              ) : null}
              <View style={styles.metaRow}>
                <Badge label={formatDuration(item.durationMinutes)} tone="info" />
                <Badge
                  label={genderText(item.gender)}
                  tone="brand"
                />
                <Badge label={`+${item.bufferMinutes} dk buffer`} tone="neutral" />
              </View>
              <View style={styles.actionsRow}>
                <Pressable
                  onPress={() => openEdit(item)}
                  hitSlop={8}
                  accessibilityRole="button"
                  accessibilityLabel="Düzenle"
                  style={styles.actionBtn}
                >
                  <Text style={styles.actionEdit}>Düzenle</Text>
                </Pressable>
                <Pressable
                  onPress={() => handleDelete(item)}
                  hitSlop={8}
                  accessibilityRole="button"
                  accessibilityLabel="Sil"
                  style={styles.actionBtn}
                  disabled={deleteMut.isPending}
                >
                  <Text style={styles.actionDelete}>Sil</Text>
                </Pressable>
              </View>
            </Card>
          </Pressable>
        )}
      />

      <Modal visible={modalOpen} transparent animationType="slide" onRequestClose={() => setModalOpen(false)}>
        <KeyboardAvoidingView
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
          style={styles.modalOverlay}
        >
          <Pressable style={{ flex: 1 }} onPress={() => setModalOpen(false)} />
          <View style={styles.modalSheet}>
            <View style={styles.handle} />
            <ScrollView contentContainerStyle={styles.modalContent} keyboardShouldPersistTaps="handled">
              <Text style={styles.modalTitle}>{editingId ? 'Hizmeti düzenle' : 'Yeni hizmet'}</Text>
              <Text style={styles.modalHint}>
                {editingId
                  ? 'Mevcut randevular etkilenmez; yeni randevular güncel fiyat/süreyle oluşur.'
                  : 'Müşterilerin görmek istediği detayları gir. Sonra düzenleyebilirsin.'}
              </Text>

              <Input
                label="Hizmet adı"
                required
                value={name}
                onChangeText={setName}
                placeholder="Örn. Saç Kesim, Boyama"
                maxLength={80}
              />

              <Text style={styles.fieldLabel}>Hangi cinsiyet?</Text>
              <View style={styles.chipsRow}>
                {GENDER_OPTIONS.map((g) => (
                  <Chip
                    key={g.value}
                    label={g.label}
                    selected={gender === g.value}
                    onPress={() => setGender(g.value)}
                  />
                ))}
              </View>

              <View style={styles.row2}>
                <View style={{ flex: 1 }}>
                  <Input
                    label="Süre (dk)"
                    required
                    value={duration}
                    onChangeText={(t) => setDuration(t.replace(/\D/g, '').slice(0, 4))}
                    keyboardType="number-pad"
                  />
                </View>
                <View style={{ flex: 1 }}>
                  <Input
                    label="Buffer (dk)"
                    value={buffer}
                    onChangeText={(t) => setBuffer(t.replace(/\D/g, '').slice(0, 3))}
                    keyboardType="number-pad"
                    hint="Sonraki randevu için ara"
                  />
                </View>
              </View>

              <Input
                label="Fiyat (TL)"
                required
                value={priceTl}
                onChangeText={(t) => setPriceTl(t.replace(/[^\d.]/g, ''))}
                keyboardType="decimal-pad"
                rightSlot={<Text style={styles.priceUnit}>₺</Text>}
              />

              <Input
                label="Açıklama"
                value={description}
                onChangeText={setDescription}
                placeholder="Müşteri ne bilmeli? (opsiyonel)"
                multiline
                numberOfLines={3}
                maxLength={500}
              />

              <View style={styles.modalActions}>
                <View style={{ flex: 1 }}>
                  <Button label="Vazgeç" variant="secondary" onPress={() => setModalOpen(false)} />
                </View>
                <View style={{ flex: 1 }}>
                  <Button
                    label={editingId ? 'Kaydet' : 'Oluştur'}
                    onPress={() => (editingId ? updateMut.mutate() : createMut.mutate())}
                    loading={editingId ? updateMut.isPending : createMut.isPending}
                    disabled={!name.trim() || !duration || !priceTl}
                  />
                </View>
              </View>
            </ScrollView>
          </View>
        </KeyboardAvoidingView>
      </Modal>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  header: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.lg,
    paddingBottom: Spacing.md,
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.md,
  },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, letterSpacing: LetterSpacing.title },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 2 },
  list: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingBottom: Spacing.xl,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  serviceHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: Spacing.sm },
  serviceName: { flex: 1, fontSize: FontSize.lg, fontWeight: FontWeight.semibold, color: Colors.text },
  servicePrice: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.ink, fontVariant: ['tabular-nums'] },
  serviceDesc: { fontSize: FontSize.sm, color: Colors.textSecondary, lineHeight: 20 },
  metaRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  actionsRow: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: Spacing.lg,
    marginTop: Spacing.xs,
    paddingTop: Spacing.xs,
    borderTopWidth: 1,
    borderTopColor: Colors.divider,
  },
  actionBtn: { paddingVertical: 6, paddingHorizontal: 4 },
  actionEdit: { fontSize: FontSize.sm, fontWeight: FontWeight.semibold, color: Colors.accent },
  actionDelete: { fontSize: FontSize.sm, fontWeight: FontWeight.semibold, color: Colors.error },

  modalOverlay: { flex: 1, backgroundColor: Colors.overlay, justifyContent: 'flex-end' },
  modalSheet: {
    backgroundColor: Colors.background,
    borderTopLeftRadius: 24,
    borderTopRightRadius: 24,
    maxHeight: '92%',
  },
  handle: {
    width: 40,
    height: 4,
    borderRadius: 2,
    backgroundColor: Colors.borderStrong,
    alignSelf: 'center',
    marginTop: 8,
    marginBottom: Spacing.sm,
  },
  modalContent: { padding: Spacing.lg, paddingBottom: Spacing.xl },
  modalTitle: { fontSize: FontSize.xl, fontWeight: FontWeight.bold, color: Colors.text },
  modalHint: { fontSize: FontSize.sm, color: Colors.textSecondary, marginTop: 4, marginBottom: Spacing.lg },
  fieldLabel: {
    fontSize: FontSize.sm,
    color: Colors.textSecondary,
    fontWeight: FontWeight.medium,
    marginBottom: 8,
  },
  chipsRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginBottom: Spacing.lg },
  row2: { flexDirection: 'row', gap: Spacing.sm },
  priceUnit: { fontSize: FontSize.lg, color: Colors.textMuted, fontWeight: FontWeight.semibold, paddingHorizontal: 4 },
  modalActions: { flexDirection: 'row', gap: Spacing.sm, marginTop: Spacing.md },
});
