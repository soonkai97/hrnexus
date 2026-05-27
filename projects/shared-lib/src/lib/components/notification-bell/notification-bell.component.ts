import { Component, OnInit, DestroyRef, signal, inject, ViewChild, ElementRef, HostListener } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, EMPTY } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Button } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { TooltipModule } from 'primeng/tooltip';
import { NotificationService } from '../../services/notification.service';
import { SignalRService } from '../../services/signalr.service';
import { AppNotification } from '../../models';

@Component({
  selector: 'nc-notification-bell',
  standalone: true,
  imports: [Button, BadgeModule, TooltipModule],
  templateUrl: './notification-bell.component.html',
  styleUrl:    './notification-bell.component.scss',
})
export class NotificationBellComponent implements OnInit {
  readonly svc               = inject(NotificationService);
  private readonly signalr   = inject(SignalRService);
  private readonly router    = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  @ViewChild('bellWrap') private bellWrap!: ElementRef<HTMLElement>;

  panelOpen = signal(false);

  ngOnInit(): void {
    // takeUntilDestroyed ensures the callback never runs on a destroyed component
    // (guards against component being torn down while the HTTP request is in-flight)
    this.svc.load().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.signalr.notifications$.pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(n => this.svc.push(n));
  }

  toggle(): void { this.panelOpen.update(v => !v); }
  close(): void  { this.panelOpen.set(false); }

  @HostListener('document:click', ['$event'])
  onDocumentClick(e: MouseEvent): void {
    if (this.panelOpen() && !this.bellWrap?.nativeElement.contains(e.target as Node)) {
      this.panelOpen.set(false);
    }
  }

  onNotifClick(n: AppNotification): void {
    if (!n.isRead) {
      this.svc.markReadOptimistic(n.id);
      // catchError in-pipe avoids nested subscribe; re-syncs state on failure
      this.svc.markRead(n.id).pipe(
        catchError(() => { this.svc.load().subscribe(); return EMPTY; }),
      ).subscribe();
    }
    const route = this.routeFor(n);
    if (route) this.router.navigate([route]);
    this.close();
  }

  markAll(): void {
    this.svc.markAllRead().pipe(
      catchError(() => { this.svc.load().subscribe(); return EMPTY; }),
    ).subscribe();
  }

  routeFor(n: AppNotification): string | null {
    switch (n.type) {
      case 'leave_approved':
      case 'leave_rejected':    return '/my-leave';
      case 'leave_pending':     return '/leave';
      case 'stale_shift':       return '/log-shift';
      case 'stale_shift_admin': return '/dashboard';
      case 'roster_assigned':
      case 'missed_clockin':    return '/log-shift';
      case 'password_reset':    return '/staff';
      default:                  return null;
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
