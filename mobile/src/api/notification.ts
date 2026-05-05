import { apiClient } from './client';

export type NotificationFeedItem = {
  id: string;
  templateKey: string;
  subject: string;
  body: string;
  isRead: boolean;
  readAt: string | null;
  createdAt: string;
};

export type NotificationFeed = {
  unreadCount: number;
  items: NotificationFeedItem[];
};

export const NotificationApi = {
  async getMine(take = 50): Promise<NotificationFeed> {
    const res = await apiClient.get<NotificationFeed>('/v1/notifications', { params: { take } });
    return res.data;
  },
  async getUnreadCount(): Promise<number> {
    const res = await apiClient.get<{ unreadCount: number }>('/v1/notifications/unread-count');
    return res.data.unreadCount;
  },
  async markRead(id: string): Promise<void> {
    await apiClient.post(`/v1/notifications/${id}/read`);
  },
  async markAllRead(): Promise<number> {
    const res = await apiClient.post<{ markedRead: number }>('/v1/notifications/read-all');
    return res.data.markedRead;
  },
};
