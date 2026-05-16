import { Component, OnInit, inject, ViewChild, ElementRef, HostListener } from '@angular/core';
import { Router } from '@angular/router';
import { Button } from 'primeng/button';
import { Popover } from 'primeng/popover';
import { BadgeModule } from 'primeng/badge';
import { TooltipModule } from 'primeng/tooltip';
import { NotificationService } from '../../services/notification.service';
import { SignalRService } from '../../services/signalr.service';
import { AppNotification } from '../../models';

@Component({
  selector: 'nc-notification-bell',
  standalone: true,
  imports: [Button, Popover, BadgeModule, TooltipModule],
  templateUrl: './notification-bell.component.html',
  styleUrl:    './notification-bell.component.scss',
})
export class NotificationBellComponent implements OnInit {
  readonly svc             = inject(NotificationService);
  private readonly signalr = inject(SignalRService);
  private readonly router  = inject(Router);

  @ViewChild('notifPanel') notifPanel!: Popover;

  ngOnInit(): void {
    this.svc.load().subscribe();
    this.signalr.notifications$.subscribe(n => this.svc.push(n));
  }

  toggle(event: Event): void {
    this.notifPanel.toggle(event);
  }

  onNotifClick(n: AppNotification): void {
    if (!n.isRead) {
      this.svc.markReadOptimistic(n.id);
      this.svc.markRead(n.id).subscribe({ error: () => this.svc.load().subscribe() });
    }
    const route = this.routeFor(n);
    if (route) this.router.navigate([route]);
    this.notifPanel.hide();
  }

  markAll(): void {
    this.svc.markAllRead().subscribe({ error: () => this.svc.load().subscribe() });
  }

  routeFor(n: AppNotification): string | null {
    switch (n.type) {
      case 'leave_approved':
      case 'leave_rejected':   return '/my-leave';
      case 'leave_pending':    return '/leave';
      case 'stale_shift':      return '/log-shift';
      case 'stale_shift_admin':return '/dashboard';
      case 'roster_assigned':
      case 'missed_clockin':   return '/log-shift';
      case 'password_reset':   return '/staff';
      default:                 return null;
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
