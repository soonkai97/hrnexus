import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { NotificationService, AppNotification } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { ProgressSpinner } from 'primeng/progressspinner';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [TranslatePipe, Button, Card, ProgressSpinner],
  templateUrl: './notifications.component.html',
  styleUrl: './notifications.component.scss',
})
export class NotificationsComponent implements OnInit {
  private readonly svc    = inject(NotificationService);
  private readonly router = inject(Router);

  readonly notifications = this.svc.notifications;
  readonly unreadCount   = this.svc.unreadCount;

  filter     = signal<'all' | 'unread'>('all');
  loading    = signal(true);
  markingAll = signal(false);

  filtered = computed(() =>
    this.filter() === 'unread'
      ? this.notifications().filter(n => !n.isRead)
      : this.notifications()
  );

  grouped = computed(() => {
    const now   = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
    const yest  = today - 86_400_000;
    const items = this.filtered();
    const groups: { label: string; items: AppNotification[] }[] = [];
    const todayItems = items.filter(n => new Date(n.createdAt).getTime() >= today);
    const yestItems  = items.filter(n => { const t = new Date(n.createdAt).getTime(); return t >= yest && t < today; });
    const olderItems = items.filter(n => new Date(n.createdAt).getTime() < yest);
    if (todayItems.length) groups.push({ label: 'Today',     items: todayItems });
    if (yestItems.length)  groups.push({ label: 'Yesterday', items: yestItems  });
    if (olderItems.length) groups.push({ label: 'Earlier',   items: olderItems });
    return groups;
  });

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
      case 'leave_pending':
      case 'leave_approved':
      case 'leave_rejected':   return '/leave';
      case 'stale_shift_admin':
      case 'roster_assigned':
      case 'missed_clockin':   return '/dashboard';
      case 'password_reset':   return '/staff';
      case 'announcement':     return '/announcements';
      case 'message':          return '/messaging';
      default:                 return null;
    }
  }

  iconFor(type: string): string {
    switch (type) {
      case 'leave_approved':  return 'pi pi-check-circle notif-type-success';
      case 'leave_rejected':  return 'pi pi-times-circle notif-type-danger';
      case 'leave_pending':   return 'pi pi-file          notif-type-info';
      case 'missed_clockin':  return 'pi pi-exclamation-triangle notif-type-warn';
      case 'stale_shift_admin': return 'pi pi-clock       notif-type-warn';
      case 'roster_assigned': return 'pi pi-calendar      notif-type-info';
      case 'password_reset':  return 'pi pi-lock          notif-type-warn';
      case 'announcement':    return 'pi pi-megaphone     notif-type-info';
      case 'message':         return 'pi pi-comments      notif-type-info';
      default:                return 'pi pi-bell          notif-type-info';
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
