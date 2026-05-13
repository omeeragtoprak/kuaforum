import { Tabs } from 'expo-router';
import { Text, View, StyleSheet } from 'react-native';
import { Colors, FontSize, FontWeight } from '@/lib/theme';
import { useUnreadNotifications } from '@/hooks/useUnreadNotifications';

function TabIcon({ icon, color }: { icon: string; color: string }) {
  return <Text style={{ fontSize: 22, color }}>{icon}</Text>;
}

function NotificationsTabIcon({ color }: { color: string }) {
  const { data } = useUnreadNotifications();
  const unread = data ?? 0;
  return (
    <View style={{ width: 32, alignItems: 'center' }}>
      <Text style={{ fontSize: 22, color }}>🔔</Text>
      {unread > 0 ? (
        <View style={badgeStyles.badge}>
          <Text style={badgeStyles.badgeText}>{unread > 9 ? '9+' : String(unread)}</Text>
        </View>
      ) : null}
    </View>
  );
}

export default function BarberLayout() {
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: '#0EA5E9',
        tabBarInactiveTintColor: Colors.textMuted,
        tabBarStyle: {
          backgroundColor: Colors.surface,
          borderTopColor: Colors.border,
          height: 64,
          paddingTop: 6,
          paddingBottom: 8,
        },
        tabBarLabelStyle: { fontSize: FontSize.xs, fontWeight: FontWeight.semibold },
      }}
    >
      <Tabs.Screen
        name="today"
        options={{
          title: 'Bugün',
          tabBarIcon: ({ color }) => <TabIcon icon="📋" color={color} />,
        }}
      />
      <Tabs.Screen
        name="calendar"
        options={{
          title: 'Takvim',
          tabBarIcon: ({ color }) => <TabIcon icon="📅" color={color} />,
        }}
      />
      <Tabs.Screen
        name="services"
        options={{
          title: 'Hizmetler',
          tabBarIcon: ({ color }) => <TabIcon icon="✂️" color={color} />,
        }}
      />
      <Tabs.Screen
        name="notifications"
        options={{
          title: 'Bildirim',
          tabBarIcon: ({ color }) => <NotificationsTabIcon color={color} />,
        }}
      />
      <Tabs.Screen
        name="profile"
        options={{
          title: 'Profil',
          tabBarIcon: ({ color }) => <TabIcon icon="👤" color={color} />,
        }}
      />
      <Tabs.Screen name="kyc" options={{ href: null }} />
      <Tabs.Screen name="availability" options={{ href: null }} />
      <Tabs.Screen name="reviews" options={{ href: null }} />
    </Tabs>
  );
}

const badgeStyles = StyleSheet.create({
  badge: {
    position: 'absolute',
    top: -4,
    right: 0,
    minWidth: 18,
    height: 18,
    paddingHorizontal: 4,
    borderRadius: 9,
    backgroundColor: '#EF4444',
    alignItems: 'center',
    justifyContent: 'center',
  },
  badgeText: {
    fontSize: 10,
    color: '#fff',
    fontWeight: FontWeight.bold,
  },
});
