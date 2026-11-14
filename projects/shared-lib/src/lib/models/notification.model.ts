export type NotificationType =
  'leave_approved' | 'leave_rejected' | 'leave_pending' | 'password_reset';

export interface AppNotification {
  id: string;
  title: string;
  message: string;
  type: NotificationType;
  isRead: boolean;
  relatedId: string | null;
  createdAt: string;
}
