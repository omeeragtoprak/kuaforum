import { useQuery } from '@tanstack/react-query';
import { NotificationApi } from '@/api/notification';
import { useAuthStore } from '@/store/auth-store';

export function useUnreadNotifications() {
  const isAuthed = useAuthStore((s) => s.isAuthenticated);
  return useQuery({
    queryKey: ['notifications-unread'],
    queryFn: () => NotificationApi.getUnreadCount(),
    enabled: isAuthed,
    refetchInterval: 30_000,
    staleTime: 15_000,
  });
}
