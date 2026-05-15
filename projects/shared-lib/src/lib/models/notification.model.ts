export type NotificationType =
  | 'leave_approved'
  | 'leave_rejected'
  | 'leave_pending'
  | 'missed_clockin'
  | 'roster_assigned'
  | 'password_reset'
  | 'message'
  | 'announcement';

export interface AppNotification {
  id: string;
  title: string;
  message: string;
  type: NotificationType;
  isRead: boolean;
  relatedId: string | null;
  createdAt: string;
}
