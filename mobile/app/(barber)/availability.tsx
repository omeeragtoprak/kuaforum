import { useMemo, useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  Pressable,
  Modal,
  Switch,
  FlatList,
  Alert,
  ActivityIndicator,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '@/components/Card';
import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { EmptyState } from '@/components/EmptyState';
import { Icon } from '@/components/Icon';
import { Colors, FontSize, FontWeight, LetterSpacing, Spacing, Radius, Layout } from '@/lib/theme';
import { BarberApi, Stylist, WorkingHours, TimeOff, DayOfWeekKey, AffectedBookingDto } from '@/api/barber';
import { extractApiError } from '@/api/client';
import { Haptics } from '@/lib/haptics';
import {
  formatWorkingHoursImpactMessage,
  formatWorkingHoursSavedImpactMessage,
  haveSameAffectedBookings,
  stylistDeactivationWarningText,
  formatStylistDeactivationImpactMessage,
} from '@/lib/salon-changes';

const DAY_LABEL: Record<DayOfWeekKey, string> = {
  Monday: 'Pazartesi',
  Tuesday: 'Salı',
  Wednesday: 'Çarşamba',
  Thursday: 'Perşembe',
  Friday: 'Cuma',
  Saturday: 'Cumartesi',
  Sunday: 'Pazar',
};

const DAY_INDEX: Record<DayOfWeekKey, number> = {
  Sunday: 0, Monday: 1, Tuesday: 2, Wednesday: 3, Thursday: 4, Friday: 5, Saturday: 6,
};

const TIME_OPTIONS = (() => {
  const out: string[] = [];
  for (let h = 6; h <= 23; h++) {
    for (const m of [0, 15, 30, 45]) {
      out.push(`${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`);
    }
  }
  return out;
})();

function fmtTime(t: string): string {
  return t.slice(0, 5);
}

function nextDays(count: number): { key: string; label: string; iso: string }[] {
  const out: { key: string; label: string; iso: string }[] = [];
  const now = new Date();
  for (let i = 0; i < count; i++) {
    const d = new Date(now);
    d.setDate(now.getDate() + i);
    d.setHours(0, 0, 0, 0);
    const iso = d.toISOString();
    const key = d.toISOString().slice(0, 10);
    const label = i === 0
      ? `Bugün, ${d.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' })}`
      : i === 1
      ? `Yarın, ${d.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' })}`
      : d.toLocaleDateString('tr-TR', { weekday: 'short', day: 'numeric', month: 'short' });
    out.push({ key, label, iso });
  }
  return out;
}

export default function AvailabilityScreen() {
  const qc = useQueryClient();
  const shopQuery = useQuery({ queryKey: ['my-shop'], queryFn: () => BarberApi.getMyShop() });
  const stylistsQuery = useQuery({
    queryKey: ['my-stylists', shopQuery.data?.id],
    queryFn: () => BarberApi.getStylists(shopQuery.data!.id),
    enabled: !!shopQuery.data?.id,
  });

  const [selectedStylistId, setSelectedStylistId] = useState<string | null>(null);
  const stylist: Stylist | null = useMemo(() => {
    const list = stylistsQuery.data ?? [];
    if (selectedStylistId) return list.find((s) => s.id === selectedStylistId) ?? null;
    return list[0] ?? null;
  }, [stylistsQuery.data, selectedStylistId]);

  const hoursQuery = useQuery({
    queryKey: ['working-hours', stylist?.id],
    queryFn: () => BarberApi.getWorkingHours(stylist!.id),
    enabled: !!stylist?.id,
  });

  const timeOffQuery = useQuery({
    queryKey: ['time-offs', stylist?.id],
    queryFn: () => BarberApi.getTimeOffs(stylist!.id),
    enabled: !!stylist?.id,
  });

  const setHoursMut = useMutation({
    mutationFn: async (body: { dayOfWeek: number; openTime: string; closeTime: string; isClosed: boolean }) => {
      if (!stylist) throw new Error('Çalışan seçilmedi.');
      return BarberApi.setWorkingHours(stylist.id, body);
    },
    onSuccess: () => { Haptics.success(); qc.invalidateQueries({ queryKey: ['working-hours', stylist?.id] }); },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const addTimeOffMut = useMutation({
    mutationFn: async (body: { startsAt: string; endsAt: string; reason?: string }) => {
      if (!stylist) throw new Error('Çalışan seçilmedi.');
      await BarberApi.addTimeOff(stylist.id, body);
    },
    onSuccess: () => { Haptics.success(); qc.invalidateQueries({ queryKey: ['time-offs', stylist?.id] }); },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const deleteTimeOffMut = useMutation({
    mutationFn: async (timeOffId: string) => {
      if (!stylist) throw new Error('Çalışan seçilmedi.');
      await BarberApi.deleteTimeOff(stylist.id, timeOffId);
    },
    onSuccess: () => { Haptics.warning(); qc.invalidateQueries({ queryKey: ['time-offs', stylist?.id] }); },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const addStylistMut = useMutation({
    mutationFn: async (displayName: string) => {
      if (!shopQuery.data?.id) throw new Error('Salon yüklenmedi.');
      const res = await BarberApi.addStylist(shopQuery.data.id, displayName);
      return res;
    },
    onSuccess: (res) => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-stylists', shopQuery.data?.id] });
      setSelectedStylistId(res.id); // yeni eklenen otomatik seçili
      setShowAddStylist(false);
      setNewStylistName('');
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const updateStylistMut = useMutation({
    mutationFn: async (args: { id: string; displayName?: string; isActive?: boolean }) => {
      const { id, ...body } = args;
      return BarberApi.updateStylist(id, body);
    },
    onSuccess: (result, variables) => {
      Haptics.success();
      qc.invalidateQueries({ queryKey: ['my-stylists', shopQuery.data?.id] });
      setEditingStylist(null);
      // RFC-015 §2 D2: yalnız FİİLEN pasifleştirme (isActive: false) yanıtı etkilenen randevu
      // taşır (backend UpdateStylistCommandHandler — yalnız gerçek aktif→pasif geçişinde
      // dolu). Rename/aktifleştirme çağrılarında affectedBookings her zaman boş gelir, bu
      // yüzden ekstra bir diyalog tetiklenmez.
      if (variables.isActive === false && result.affectedBookings.length > 0) {
        const name = stylistsQuery.data?.find((s) => s.id === variables.id)?.displayName ?? 'Çalışan';
        Alert.alert('Etkilenen randevular', formatStylistDeactivationImpactMessage(result.affectedBookings, name));
      }
    },
    onError: (err) => { Haptics.error(); Alert.alert('Hata', extractApiError(err)); },
  });

  const [showAddStylist, setShowAddStylist] = useState(false);
  const [newStylistName, setNewStylistName] = useState('');
  const [editingStylist, setEditingStylist] = useState<Stylist | null>(null);
  const [editingName, setEditingName] = useState('');

  const [editing, setEditing] = useState<WorkingHours | null>(null);
  const [timePickerFor, setTimePickerFor] = useState<'open' | 'close' | null>(null);
  const [editingOpen, setEditingOpen] = useState('09:00');
  const [editingClose, setEditingClose] = useState('19:00');
  const [editingClosed, setEditingClosed] = useState(false);
  // RFC-015 §2 D1 madde 2 — kaydetmeden önceki salt-okunur önizleme çağrısı sırasında "Kaydet"
  // butonunu meşgul göster (setHoursMut.isPending ayrı bir aşama, bu ondan önce gelir).
  const [checkingImpact, setCheckingImpact] = useState(false);

  const [showAddTimeOff, setShowAddTimeOff] = useState(false);
  const [toDay, setToDay] = useState<string | null>(null);
  const [toStart, setToStart] = useState('09:00');
  const [toEnd, setToEnd] = useState('18:00');
  const [toReason, setToReason] = useState('');
  const [toDayPicker, setToDayPicker] = useState(false);
  const [toTimePickerFor, setToTimePickerFor] = useState<'start' | 'end' | null>(null);

  function openHoursEditor(wh: WorkingHours) {
    setEditing(wh);
    setEditingOpen(fmtTime(wh.openTime));
    setEditingClose(fmtTime(wh.closeTime));
    setEditingClosed(wh.isClosed);
  }

  // RFC-015 §2 D1: kaydetmeden önce salt-okunur önizleme çağrılır. Etkilenen randevu varsa
  // kuaföre "ne olacağını" anlatan bir onay diyaloğu gösterilir — "emin misiniz?" DEMEZ, kaç
  // randevu + hangi müşteri/saat etkilendiğini yazar. Onaylarsa kaydedilir; ENGELLEME YOK.
  // Önizleme hata verirse akışı BLOKLAMAZ — doğrudan kaydetmeye izin verir (madde 4, tavsiye
  // niteliğinde bir kontrol).
  async function saveHours() {
    if (!editing || !stylist) return;
    if (!editingClosed) {
      const [oh, om] = editingOpen.split(':').map(Number);
      const [ch, cm] = editingClose.split(':').map(Number);
      if (oh * 60 + om >= ch * 60 + cm) {
        Alert.alert('Geçersiz', 'Açılış saati kapanış saatinden önce olmalı.');
        return;
      }
    }

    const body = {
      dayOfWeek: DAY_INDEX[editing.dayOfWeek],
      openTime: `${editingOpen}:00`,
      closeTime: `${editingClose}:00`,
      isClosed: editingClosed,
    };

    let previewAffected: AffectedBookingDto[] = [];
    setCheckingImpact(true);
    try {
      previewAffected = await BarberApi.previewWorkingHoursImpact(stylist.id, body);
    } catch {
      previewAffected = []; // bloklama — kaydetmeye izin ver, sonucu save yanıtı belirleyecek
    } finally {
      setCheckingImpact(false);
    }

    if (previewAffected.length > 0) {
      Alert.alert(
        'Değişikliği kaydet',
        formatWorkingHoursImpactMessage(previewAffected),
        [
          { text: 'Vazgeç', style: 'cancel' },
          { text: 'Kaydet', onPress: () => commitHours(body, previewAffected) },
        ],
      );
      return;
    }

    commitHours(body, previewAffected);
  }

  function commitHours(
    body: { dayOfWeek: number; openTime: string; closeTime: string; isClosed: boolean },
    previewAffected: AffectedBookingDto[],
  ) {
    setHoursMut.mutate(body, {
      onSuccess: (result) => {
        setEditing(null);
        // Madde 3: preview ile save arasında fark çıkarsa (yarış — yeni randevu girdi/çıktı)
        // kullanıcıya SON DURUM gösterilir. Aynıysa (normal yol — kullanıcı zaten yukarıdaki
        // diyalogda gördü) ikinci kez gösterilmez.
        if (!haveSameAffectedBookings(result.affectedBookings, previewAffected) && result.affectedBookings.length > 0) {
          Alert.alert('Bilgi', formatWorkingHoursSavedImpactMessage(result.affectedBookings));
        }
      },
    });
  }

  function saveTimeOff() {
    if (!toDay) {
      Alert.alert('Eksik', 'Bir gün seç.');
      return;
    }
    const [sh, sm] = toStart.split(':').map(Number);
    const [eh, em] = toEnd.split(':').map(Number);
    if (sh * 60 + sm >= eh * 60 + em) {
      Alert.alert('Geçersiz', 'Başlangıç saati bitiş saatinden önce olmalı.');
      return;
    }
    const day = new Date(toDay);
    const start = new Date(day);
    start.setHours(sh, sm, 0, 0);
    const end = new Date(day);
    end.setHours(eh, em, 0, 0);
    addTimeOffMut.mutate({
      startsAt: start.toISOString(),
      endsAt: end.toISOString(),
      reason: toReason.trim() || undefined,
    }, {
      onSuccess: () => {
        setShowAddTimeOff(false);
        setToDay(null);
        setToReason('');
      },
    });
  }

  function confirmDeleteTimeOff(t: TimeOff) {
    Alert.alert(
      'İzni sil',
      `${formatRange(t)} silinsin mi?`,
      [
        { text: 'Vazgeç', style: 'cancel' },
        { text: 'Sil', style: 'destructive', onPress: () => deleteTimeOffMut.mutate(t.id) },
      ],
    );
  }

  if (shopQuery.isLoading || stylistsQuery.isLoading) {
    return (
      <SafeAreaView style={styles.container}>
        <ActivityIndicator size="large" color={Colors.ink} style={{ marginTop: 80 }} />
      </SafeAreaView>
    );
  }

  // G12 fix: shopQuery hatası (ağ/5xx) eskiden `!shopQuery.data` dalına düşüp "salon yok"
  // YALANI söylüyordu — `getMyShop()` artık gerçek hatayı yutmuyor (bkz. api/barber.ts).
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
        <EmptyState icon="store" title="Salon yok" description="Önce profil sekmesinden salon bilgilerini gir." />
      </SafeAreaView>
    );
  }

  const stylists = stylistsQuery.data ?? [];

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScrollView contentContainerStyle={styles.scroll} showsVerticalScrollIndicator={false}>
        <Text style={styles.title}>Müsaitlik</Text>
        <Text style={styles.subtitle}>Çalışma saatlerini ve izin günlerini buradan yönet.</Text>

        <View style={styles.stylistChips}>
          {stylists.map((s) => {
            const active = (selectedStylistId ?? stylists[0]?.id) === s.id;
            return (
              <Pressable
                key={s.id}
                onPress={() => setSelectedStylistId(s.id)}
                onLongPress={() => {
                  setEditingStylist(s);
                  setEditingName(s.displayName);
                }}
                delayLongPress={400}
                style={[styles.chip, active && styles.chipActive]}
                accessibilityRole="button"
                accessibilityLabel={`Çalışan: ${s.displayName}. Düzenlemek için uzun bas.`}
              >
                <Text style={[styles.chipText, active && styles.chipTextActive]}>{s.displayName}</Text>
              </Pressable>
            );
          })}
          <Pressable
            onPress={() => setShowAddStylist(true)}
            style={[styles.chip, styles.chipAdd]}
            accessibilityRole="button"
            accessibilityLabel="Yeni çalışan ekle"
          >
            <Icon name="plus" size={13} color={Colors.ink} strokeWidth={2} />
            <Text style={styles.chipAddText}>Yeni çalışan</Text>
          </Pressable>
        </View>

        <Text style={styles.sectionTitle}>Çalışma saatleri</Text>
        <Card>
          {hoursQuery.isLoading ? (
            <ActivityIndicator color={Colors.ink} />
          ) : (
            (hoursQuery.data ?? []).map((wh, idx) => (
              <Pressable
                key={wh.dayOfWeek}
                onPress={() => openHoursEditor(wh)}
                style={[styles.dayRow, idx > 0 && styles.dayRowBorder]}
                accessibilityRole="button"
                accessibilityLabel={`${DAY_LABEL[wh.dayOfWeek]}: ${wh.isClosed ? 'kapalı' : `${fmtTime(wh.openTime)} - ${fmtTime(wh.closeTime)}`}`}
              >
                <Text style={styles.dayName}>{DAY_LABEL[wh.dayOfWeek]}</Text>
                <Text style={[styles.dayValue, wh.isClosed && styles.dayClosed]}>
                  {wh.isClosed ? 'Kapalı' : `${fmtTime(wh.openTime)} – ${fmtTime(wh.closeTime)}`}
                </Text>
                <Icon name="chevronRight" size={16} color={Colors.textMuted} />
              </Pressable>
            ))
          )}
        </Card>

        <View style={styles.sectionHeaderRow}>
          <Text style={styles.sectionTitle}>İzin günlerim</Text>
          <Pressable onPress={() => setShowAddTimeOff(true)} style={styles.addBtn} accessibilityRole="button">
            <Icon name="plus" size={13} color={Colors.ink} strokeWidth={2} />
            <Text style={styles.addBtnText}>Yeni</Text>
          </Pressable>
        </View>
        <Card>
          {timeOffQuery.isLoading ? (
            <ActivityIndicator color={Colors.ink} />
          ) : (timeOffQuery.data ?? []).length === 0 ? (
            <Text style={styles.emptyTimeOff}>Planlanmış izin yok.</Text>
          ) : (
            (timeOffQuery.data ?? []).map((t, idx) => (
              <Pressable
                key={t.id}
                onPress={() => confirmDeleteTimeOff(t)}
                style={[styles.timeOffRow, idx > 0 && styles.dayRowBorder]}
                accessibilityRole="button"
                accessibilityLabel={`İzin ${formatRange(t)}, silmek için bas.`}
              >
                <View style={{ flex: 1 }}>
                  <Text style={styles.timeOffRange}>{formatRange(t)}</Text>
                  {t.reason ? <Text style={styles.timeOffReason}>{t.reason}</Text> : null}
                </View>
                <Icon name="x" size={16} color={Colors.error} />
              </Pressable>
            ))
          )}
        </Card>

        <View style={styles.noteRow}>
          <Icon name="info" size={13} color={Colors.textMuted} />
          <Text style={styles.note}>
            İzin saatlerin müşteriye kapalı olarak gösterilir. Geçmiş randevular etkilenmez.
          </Text>
        </View>
      </ScrollView>

      <Modal visible={!!editing} transparent animationType="slide" onRequestClose={() => setEditing(null)}>
        <Pressable style={styles.modalBackdrop} onPress={() => setEditing(null)}>
          <Pressable style={styles.modalCard} onPress={(e) => e.stopPropagation()}>
            <Text style={styles.modalTitle}>{editing ? DAY_LABEL[editing.dayOfWeek] : ''}</Text>

            <View style={styles.switchRow}>
              <Text style={styles.switchLabel}>Bu gün kapalı</Text>
              <Switch
                value={editingClosed}
                onValueChange={setEditingClosed}
                trackColor={{ true: Colors.ink, false: Colors.border }}
              />
            </View>

            {!editingClosed ? (
              <View style={styles.timeRow}>
                <Pressable onPress={() => setTimePickerFor('open')} style={[styles.timeBox, styles.timeBoxFlex]}>
                  <Text style={styles.timeBoxLabel}>Açılış</Text>
                  <Text style={styles.timeBoxValue}>{editingOpen}</Text>
                </Pressable>
                <Text style={styles.dash}>–</Text>
                <Pressable onPress={() => setTimePickerFor('close')} style={[styles.timeBox, styles.timeBoxFlex]}>
                  <Text style={styles.timeBoxLabel}>Kapanış</Text>
                  <Text style={styles.timeBoxValue}>{editingClose}</Text>
                </Pressable>
              </View>
            ) : null}

            <View style={styles.modalActions}>
              <Button label="Vazgeç" variant="ghost" onPress={() => setEditing(null)} />
              <Button label="Kaydet" onPress={saveHours} loading={checkingImpact || setHoursMut.isPending} />
            </View>
          </Pressable>
        </Pressable>
      </Modal>

      <TimeListModal
        visible={timePickerFor !== null}
        title={timePickerFor === 'open' ? 'Açılış saati' : 'Kapanış saati'}
        value={timePickerFor === 'open' ? editingOpen : editingClose}
        onSelect={(t) => {
          if (timePickerFor === 'open') setEditingOpen(t);
          else if (timePickerFor === 'close') setEditingClose(t);
          setTimePickerFor(null);
        }}
        onClose={() => setTimePickerFor(null)}
      />

      <Modal visible={showAddTimeOff} transparent animationType="slide" onRequestClose={() => setShowAddTimeOff(false)}>
        <Pressable style={styles.modalBackdrop} onPress={() => setShowAddTimeOff(false)}>
          <Pressable style={styles.modalCard} onPress={(e) => e.stopPropagation()}>
            <Text style={styles.modalTitle}>Yeni izin</Text>
            <Pressable onPress={() => setToDayPicker(true)} style={styles.timeBox}>
              <Text style={styles.timeBoxLabel}>Gün</Text>
              <Text style={styles.timeBoxValue}>
                {toDay ? new Date(toDay).toLocaleDateString('tr-TR', { weekday: 'short', day: 'numeric', month: 'short' }) : 'Seç'}
              </Text>
            </Pressable>

            <View style={styles.timeRow}>
              <Pressable onPress={() => setToTimePickerFor('start')} style={[styles.timeBox, styles.timeBoxFlex]}>
                <Text style={styles.timeBoxLabel}>Başlangıç</Text>
                <Text style={styles.timeBoxValue}>{toStart}</Text>
              </Pressable>
              <Text style={styles.dash}>–</Text>
              <Pressable onPress={() => setToTimePickerFor('end')} style={[styles.timeBox, styles.timeBoxFlex]}>
                <Text style={styles.timeBoxLabel}>Bitiş</Text>
                <Text style={styles.timeBoxValue}>{toEnd}</Text>
              </Pressable>
            </View>

            <Input
              label="Sebep (opsiyonel)"
              value={toReason}
              onChangeText={setToReason}
              placeholder="örn: Doktor randevusu"
            />

            <View style={styles.modalActions}>
              <Button label="Vazgeç" variant="ghost" onPress={() => setShowAddTimeOff(false)} />
              <Button label="Kaydet" onPress={saveTimeOff} loading={addTimeOffMut.isPending} />
            </View>
          </Pressable>
        </Pressable>
      </Modal>

      <DayListModal
        visible={toDayPicker}
        value={toDay}
        onSelect={(iso) => {
          setToDay(iso);
          setToDayPicker(false);
        }}
        onClose={() => setToDayPicker(false)}
      />

      <TimeListModal
        visible={toTimePickerFor !== null}
        title={toTimePickerFor === 'start' ? 'Başlangıç saati' : 'Bitiş saati'}
        value={toTimePickerFor === 'start' ? toStart : toEnd}
        onSelect={(t) => {
          if (toTimePickerFor === 'start') setToStart(t);
          else if (toTimePickerFor === 'end') setToEnd(t);
          setToTimePickerFor(null);
        }}
        onClose={() => setToTimePickerFor(null)}
      />

      <Modal visible={showAddStylist} transparent animationType="slide" onRequestClose={() => setShowAddStylist(false)}>
        <Pressable style={styles.modalBackdrop} onPress={() => setShowAddStylist(false)}>
          <Pressable style={styles.modalCard} onPress={(e) => e.stopPropagation()}>
            <Text style={styles.modalTitle}>Yeni çalışan</Text>
            <Text style={styles.stylistHint}>
              Eklediğin çalışana otomatik olarak Pzt-Cmt 09:00-19:00 çalışma saatleri atanır.
              Sonradan değiştirebilirsin.
            </Text>
            <Input
              label="Görünen ad"
              value={newStylistName}
              onChangeText={setNewStylistName}
              placeholder="Örn: Selin Kaya"
              maxLength={80}
            />
            <View style={styles.modalActions}>
              <Button label="Vazgeç" variant="ghost" onPress={() => { setShowAddStylist(false); setNewStylistName(''); }} />
              <Button
                label="Ekle"
                onPress={() => {
                  const name = newStylistName.trim();
                  if (name.length < 2) {
                    Alert.alert('Geçersiz', 'En az 2 karakter girmelisin.');
                    return;
                  }
                  addStylistMut.mutate(name);
                }}
                loading={addStylistMut.isPending}
              />
            </View>
          </Pressable>
        </Pressable>
      </Modal>

      <Modal visible={!!editingStylist} transparent animationType="slide" onRequestClose={() => setEditingStylist(null)}>
        <Pressable style={styles.modalBackdrop} onPress={() => setEditingStylist(null)}>
          <Pressable style={styles.modalCard} onPress={(e) => e.stopPropagation()}>
            <Text style={styles.modalTitle}>Çalışanı düzenle</Text>
            <Input
              label="Görünen ad"
              value={editingName}
              onChangeText={setEditingName}
              maxLength={80}
            />
            <View style={styles.stylistDangerRow}>
              <Text style={styles.stylistDangerLabel}>
                {editingStylist?.isActive ? 'Aktif çalışıyor' : 'Pasif (görünmüyor)'}
              </Text>
              <Pressable
                style={editingStylist?.isActive ? styles.stylistDeactivateBtn : styles.stylistActivateBtn}
                onPress={() => {
                  if (!editingStylist) return;
                  Alert.alert(
                    editingStylist.isActive ? 'Çalışanı pasifleştir' : 'Çalışanı aktif et',
                    editingStylist.isActive
                      ? stylistDeactivationWarningText()
                      : 'Çalışan tekrar müşterilere görünmeye başlar.',
                    [
                      { text: 'Vazgeç', style: 'cancel' },
                      {
                        text: editingStylist.isActive ? 'Pasifleştir' : 'Aktif et',
                        style: editingStylist.isActive ? 'destructive' : 'default',
                        onPress: () => updateStylistMut.mutate({ id: editingStylist.id, isActive: !editingStylist.isActive }),
                      },
                    ],
                  );
                }}
              >
                <Text style={editingStylist?.isActive ? styles.stylistDeactivateText : styles.stylistActivateText}>
                  {editingStylist?.isActive ? 'Pasifleştir' : 'Aktif et'}
                </Text>
              </Pressable>
            </View>
            <View style={styles.modalActions}>
              <Button label="Vazgeç" variant="ghost" onPress={() => setEditingStylist(null)} />
              <Button
                label="Kaydet"
                onPress={() => {
                  if (!editingStylist) return;
                  const name = editingName.trim();
                  if (name.length < 2) {
                    Alert.alert('Geçersiz', 'En az 2 karakter girmelisin.');
                    return;
                  }
                  if (name === editingStylist.displayName) {
                    setEditingStylist(null);
                    return;
                  }
                  updateStylistMut.mutate({ id: editingStylist.id, displayName: name });
                }}
                loading={updateStylistMut.isPending}
              />
            </View>
          </Pressable>
        </Pressable>
      </Modal>
    </SafeAreaView>
  );
}

function formatRange(t: TimeOff): string {
  const s = new Date(t.startsAt);
  const e = new Date(t.endsAt);
  const sameDay = s.toDateString() === e.toDateString();
  const dateStr = s.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' });
  const sTime = s.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
  const eTime = e.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
  if (sameDay) return `${dateStr} · ${sTime}–${eTime}`;
  const eDate = e.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' });
  return `${dateStr} ${sTime} → ${eDate} ${eTime}`;
}

function TimeListModal({
  visible,
  title,
  value,
  onSelect,
  onClose,
}: {
  visible: boolean;
  title: string;
  value: string;
  onSelect: (t: string) => void;
  onClose: () => void;
}) {
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.modalBackdrop} onPress={onClose}>
        <Pressable style={styles.modalCard} onPress={(e) => e.stopPropagation()}>
          <Text style={styles.modalTitle}>{title}</Text>
          <FlatList
            data={TIME_OPTIONS}
            keyExtractor={(t) => t}
            style={{ maxHeight: 320 }}
            renderItem={({ item }) => {
              const active = item === value;
              return (
                <Pressable
                  onPress={() => onSelect(item)}
                  style={[styles.timeOption, active && styles.timeOptionActive]}
                >
                  <Text style={[styles.timeOptionText, active && styles.timeOptionTextActive]}>{item}</Text>
                </Pressable>
              );
            }}
          />
          <Button label="Kapat" variant="ghost" onPress={onClose} />
        </Pressable>
      </Pressable>
    </Modal>
  );
}

function DayListModal({
  visible,
  value,
  onSelect,
  onClose,
}: {
  visible: boolean;
  value: string | null;
  onSelect: (iso: string) => void;
  onClose: () => void;
}) {
  const days = useMemo(() => nextDays(30), []);
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.modalBackdrop} onPress={onClose}>
        <Pressable style={styles.modalCard} onPress={(e) => e.stopPropagation()}>
          <Text style={styles.modalTitle}>Gün seç</Text>
          <FlatList
            data={days}
            keyExtractor={(d) => d.key}
            style={{ maxHeight: 360 }}
            renderItem={({ item }) => {
              const active = value && value.slice(0, 10) === item.key;
              return (
                <Pressable
                  onPress={() => onSelect(item.iso)}
                  style={[styles.timeOption, active && styles.timeOptionActive]}
                >
                  <Text style={[styles.timeOptionText, active && styles.timeOptionTextActive]}>{item.label}</Text>
                </Pressable>
              );
            }}
          />
          <Button label="Kapat" variant="ghost" onPress={onClose} />
        </Pressable>
      </Pressable>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: Colors.background },
  scroll: {
    paddingHorizontal: Layout.screenPaddingX,
    paddingTop: Spacing.lg,
    paddingBottom: Spacing.xxl,
    gap: Spacing.md,
    maxWidth: Layout.maxContentWidth,
    width: '100%',
    alignSelf: 'center',
  },
  title: { fontSize: FontSize.xxl, fontWeight: FontWeight.bold, color: Colors.text, letterSpacing: LetterSpacing.title },
  subtitle: { fontSize: FontSize.sm, color: Colors.textSecondary, marginBottom: Spacing.sm },

  stylistChips: { flexDirection: 'row', flexWrap: 'wrap', gap: Spacing.sm, marginBottom: Spacing.sm },
  chip: {
    paddingHorizontal: Spacing.md,
    paddingVertical: 8,
    borderRadius: Radius.full,
    backgroundColor: Colors.surface,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  chipActive: { backgroundColor: Colors.ink, borderColor: Colors.ink },
  chipText: { fontSize: FontSize.sm, color: Colors.text, fontWeight: FontWeight.semibold },
  chipTextActive: { color: Colors.textInverse },
  chipAdd: {
    flexDirection: 'row', alignItems: 'center', gap: 4,
    backgroundColor: Colors.surfaceAlt, borderStyle: 'dashed', borderColor: Colors.borderStrong,
  },
  chipAddText: { fontSize: FontSize.sm, color: Colors.ink, fontWeight: FontWeight.bold },

  stylistHint: { fontSize: FontSize.xs, color: Colors.textSecondary, lineHeight: 16, marginBottom: Spacing.sm },
  stylistDangerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: Spacing.sm,
    marginTop: Spacing.sm,
    borderTopWidth: 1,
    borderTopColor: Colors.divider,
    paddingTop: Spacing.md,
  },
  stylistDangerLabel: { fontSize: FontSize.sm, color: Colors.textSecondary },
  stylistDeactivateBtn: {
    paddingHorizontal: Spacing.md,
    paddingVertical: 6,
    borderRadius: Radius.full,
    backgroundColor: Colors.errorSoft,
  },
  stylistDeactivateText: { fontSize: FontSize.xs, color: Colors.error, fontWeight: FontWeight.bold },
  stylistActivateBtn: {
    paddingHorizontal: Spacing.md,
    paddingVertical: 6,
    borderRadius: Radius.full,
    backgroundColor: Colors.successSoft,
  },
  stylistActivateText: { fontSize: FontSize.xs, color: Colors.success, fontWeight: FontWeight.bold },

  sectionTitle: {
    fontSize: FontSize.lg,
    fontWeight: FontWeight.bold,
    color: Colors.text,
    marginTop: Spacing.md,
    marginBottom: Spacing.sm,
  },
  sectionHeaderRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: Spacing.md },
  addBtn: {
    flexDirection: 'row', alignItems: 'center', gap: 4,
    paddingHorizontal: Spacing.md, paddingVertical: 6, borderRadius: Radius.full, backgroundColor: Colors.surfaceAlt,
  },
  addBtnText: { fontSize: FontSize.sm, fontWeight: FontWeight.semibold, color: Colors.ink },

  dayRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: Spacing.md,
    gap: Spacing.md,
  },
  dayRowBorder: { borderTopWidth: 1, borderTopColor: Colors.border },
  dayName: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text, width: 110 },
  dayValue: { flex: 1, fontSize: FontSize.md, color: Colors.text, textAlign: 'right' },
  dayClosed: { color: Colors.textMuted, fontStyle: 'italic' },

  emptyTimeOff: { fontSize: FontSize.sm, color: Colors.textMuted, paddingVertical: Spacing.md, textAlign: 'center' },
  timeOffRow: { flexDirection: 'row', alignItems: 'center', paddingVertical: Spacing.md, gap: Spacing.md },
  timeOffRange: { fontSize: FontSize.md, fontWeight: FontWeight.semibold, color: Colors.text },
  timeOffReason: { fontSize: FontSize.xs, color: Colors.textMuted, marginTop: 2 },

  noteRow: { flexDirection: 'row', alignItems: 'flex-start', gap: 6, marginTop: Spacing.md },
  note: { flex: 1, fontSize: FontSize.xs, color: Colors.textMuted, lineHeight: 16 },

  modalBackdrop: {
    flex: 1,
    backgroundColor: Colors.overlay,
    justifyContent: 'flex-end',
  },
  modalCard: {
    backgroundColor: Colors.surface,
    borderTopLeftRadius: Radius.lg,
    borderTopRightRadius: Radius.lg,
    padding: Spacing.lg,
    gap: Spacing.md,
    maxHeight: '85%',
  },
  modalTitle: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  switchRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  switchLabel: { fontSize: FontSize.md, color: Colors.text },

  timeRow: { flexDirection: 'row', alignItems: 'center', gap: Spacing.sm },
  timeBox: {
    backgroundColor: Colors.background,
    borderRadius: Radius.md,
    padding: Spacing.md,
    borderWidth: 1,
    borderColor: Colors.border,
  },
  timeBoxFlex: { flex: 1 },
  timeBoxLabel: { fontSize: FontSize.xs, color: Colors.textMuted, marginBottom: 4 },
  timeBoxValue: { fontSize: FontSize.lg, fontWeight: FontWeight.bold, color: Colors.text },
  dash: { fontSize: FontSize.xl, color: Colors.textMuted },

  modalActions: { flexDirection: 'row', gap: Spacing.sm, justifyContent: 'flex-end', marginTop: Spacing.md },

  timeOption: { paddingVertical: 14, paddingHorizontal: Spacing.md, borderRadius: Radius.md },
  timeOptionActive: { backgroundColor: Colors.surfaceAlt },
  timeOptionText: { fontSize: FontSize.md, color: Colors.text },
  timeOptionTextActive: { color: Colors.ink, fontWeight: FontWeight.bold },
});
