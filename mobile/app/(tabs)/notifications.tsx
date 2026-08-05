import { NotificationsScreen } from '@/screens/NotificationsScreen';
import { AuthPrompt } from '@/components/AuthPrompt';
import { Colors } from '@/lib/theme';
import { useAuthStore } from '@/store/auth-store';

export default function CustomerNotifications() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);

  // RFC-010 — misafir "bildirimler" sekmesine geldiğinde global redirect yerine
  // burada AuthPrompt gösterilir; girişten sonra aynı sekmeye döner.
  if (!isAuthenticated) {
    return (
      <AuthPrompt
        description="Bildirimlerini görmek için giriş yapman gerekiyor."
        redirectTo="/(tabs)/notifications"
      />
    );
  }

  return <NotificationsScreen accent={Colors.primary} />;
}
