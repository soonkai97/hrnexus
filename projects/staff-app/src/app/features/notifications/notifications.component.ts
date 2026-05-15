import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NotificationService, AppNotification } from 'shared-lib';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './notifications.component.html',
  styleUrl: './notifications.component.scss',
})
export class NotificationsComponent implements OnInit {
  private readonly svc    = inject(NotificationService);
  private readonly router = inject(Router);

  readonly notifications = this.svc.notifications;
  readonly unreadCount   = this.svc.unreadCount;

  filter   = signal<'all' | 'unread'>('all');
  loading  = signal(true);
  markingAll = signal(false);

  filtered = computed(() =>
    this.filter() === 'unread'
      ? this.notifications().filter(n => !n.isRead)
      : this.notifications()
  );

  ngOnInit(): void {
    this.svc.load().subscribe({ complete: () => this.loading.set(false) });
  }

  onNotifClick(n: AppNotification): void {
    if (!n.isRead) {
      this.svc.markReadOptimistic(n.id);
      this.svc.markRead(n.id).subscribe({ error: () => this.svc.load().subscribe() });
    }
    const route = this.routeFor(n);
    if (route) this.router.navigate([route]);
  }

  markAll(): void {
    this.markingAll.set(true);
    this.svc.markAllRead().subscribe({
      error:    () => this.svc.load().subscribe(),
      complete: () => this.markingAll.set(false),
    });
  }

  routeFor(n: AppNotification): string | null {
    switch (n.type) {
      case 'leave_approved':
      case 'leave_rejected':
      case 'leave_pending':   return '/my-leave';
      case 'roster_assigned':
      case 'missed_clockin':
      case 'stale_shift':     return '/log-shift';
      case 'password_reset':  return '/profile';
      case 'announcement':    return '/announcements';
      case 'message':         return '/messaging';
      default:                return null;
    }
  }

  iconFor(type: string): string {
    switch (type) {
      case 'leave_approved':  return '✅';
      case 'leave_rejected':  return '❌';
      case 'leave_pending':   return '📋';
      case 'missed_clockin':  return '⚠️';
      case 'roster_assigned': return '📅';
      case 'password_reset':  return '🔐';
      case 'announcement':    return '📢';
      case 'message':         return '💬';
      default:                return '🔔';
    }
  }

  timeAgo(isoStr: string): string {
    const diff = Date.now() - new Date(isoStr).getTime();
    const mins = Math.floor(diff / 60_000);
    if (mins < 1)  return 'just now';
    if (mins < 60) return `${mins}m ago`;
    const hrs = Math.floor(mins / 60);
    if (hrs < 24)  return `${hrs}h ago`;
    const days = Math.floor(hrs / 24);
    return days === 1 ? 'yesterday' : `${days}d ago`;
  }
}
