import { Component, OnInit, inject, signal, ViewChild, ElementRef, HostListener } from '@angular/core';
import { Router } from '@angular/router';
import { NotificationService } from '../../services/notification.service';
import { SignalRService } from '../../services/signalr.service';
import { AppNotification } from '../../models';

@Component({
  selector: 'nc-notification-bell',
  standalone: true,
  imports: [],
  templateUrl: './notification-bell.component.html',
  styleUrl:    './notification-bell.component.scss',
})
export class NotificationBellComponent implements OnInit {
  readonly svc             = inject(NotificationService);
  private readonly signalr = inject(SignalRService);
  private readonly router  = inject(Router);
  private readonly el      = inject(ElementRef);

  @ViewChild('bellBtn') bellBtn!: ElementRef<HTMLButtonElement>;

  open       = signal(false);
  panelTop   = 0;
  panelRight = 0;

  ngOnInit(): void {
    this.svc.load().subscribe();
    this.signalr.notifications$.subscribe(n => this.svc.push(n));
  }

  /**
   * Close the panel when clicking anywhere outside this component.
   * Uses ElementRef.contains() — works regardless of CSS backdrop-filter
   * or any other stacking context on ancestor elements.
   */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.el.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }

  toggle(): void {
    if (this.open()) {
      this.close();
    } else {
      this.updatePanelPosition();
      this.open.set(true);
    }
  }

  close(): void { this.open.set(false); }

  /** Anchor the panel just below the bell button, clamped to stay on-screen. */
  private updatePanelPosition(): void {
    const rect       = this.bellBtn.nativeElement.getBoundingClientRect();
    const panelWidth = Math.min(340, window.innerWidth - 16);
    const margin     = 8;

    this.panelTop = rect.bottom + 8;

    // Right-align panel to the bell button, but ensure it doesn't overflow left
    let right = window.innerWidth - rect.right;
    const leftEdge = window.innerWidth - right - panelWidth;
    if (leftEdge < margin) {
      right = window.innerWidth - panelWidth - margin;
    }
    this.panelRight = Math.max(margin, right);
  }

  onNotifClick(n: AppNotification): void {
    if (!n.isRead) {
      this.svc.markReadOptimistic(n.id);
      this.svc.markRead(n.id).subscribe({ error: () => this.svc.load().subscribe() });
    }
    const route = this.routeFor(n);
    if (route) this.router.navigate([route]);
    this.close();
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
