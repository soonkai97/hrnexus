import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { NotificationsComponent } from './notifications.component';
import { NotificationService } from 'shared-lib';

/**
 * NotificationsComponent — admin notification centre.
 *
 * WHY these tests matter:
 *  - routeFor() drives navigation on click; a wrong route for 'leave_pending'
 *    sends the manager to the wrong page and they can't act on the request.
 *  - timeAgo() is the only relative-time display in the app; wrong branch
 *    thresholds mean "1h ago" shows for a 59-minute-old notification.
 *  - filtered() unread gate ensures the "Unread only" tab shows the right count.
 *  - grouped() time bucketing must correctly categorise today/yesterday/earlier
 *    so managers can quickly spot which notifications need attention now.
 */

function makeNotif(overrides: Partial<any> = {}): any {
  return {
    id: 'n1', title: 'Test', message: 'msg', type: 'leave_pending',
    isRead: false, relatedId: null, createdAt: new Date().toISOString(),
    ...overrides,
  };
}

describe('NotificationsComponent', () => {
  let component: NotificationsComponent;

  let mockNotifSvc: {
    notifications:       ReturnType<typeof vi.fn>;
    unreadCount:         ReturnType<typeof vi.fn>;
    load:                ReturnType<typeof vi.fn>;
    markRead:            ReturnType<typeof vi.fn>;
    markReadOptimistic:  ReturnType<typeof vi.fn>;
    markAllRead:         ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    mockNotifSvc = {
      notifications:      vi.fn().mockReturnValue([]),
      unreadCount:        vi.fn().mockReturnValue(0),
      load:               vi.fn().mockReturnValue(of([])),
      markRead:           vi.fn().mockReturnValue(of(undefined)),
      markReadOptimistic: vi.fn(),
      markAllRead:        vi.fn().mockReturnValue(of(undefined)),
    };

    await TestBed.configureTestingModule({
      imports: [NotificationsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        // Override Router so navigate() doesn't throw NG04002 (no routes defined)
        { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: NotificationService, useValue: mockNotifSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(NotificationsComponent).componentInstance;
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  // ── timeAgo() ─────────────────────────────────────────────────────────────────

  describe('timeAgo()', () => {
    beforeEach(() => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-05-17T12:00:00.000Z'));
    });

    it('returns "just now" for less than 1 minute ago', () => {
      const t = new Date('2026-05-17T11:59:30.000Z').toISOString();
      expect(component.timeAgo(t)).toBe('just now');
    });

    it('returns "Xm ago" for minutes less than 60', () => {
      const t = new Date('2026-05-17T11:30:00.000Z').toISOString(); // 30 min ago
      expect(component.timeAgo(t)).toBe('30m ago');
    });

    it('returns "Xh ago" for hours less than 24', () => {
      const t = new Date('2026-05-17T09:00:00.000Z').toISOString(); // 3h ago
      expect(component.timeAgo(t)).toBe('3h ago');
    });

    it('returns "yesterday" for exactly 1 day ago', () => {
      const t = new Date('2026-05-16T12:00:00.000Z').toISOString(); // 24h ago
      expect(component.timeAgo(t)).toBe('yesterday');
    });

    it('returns "Xd ago" for more than 1 day ago', () => {
      const t = new Date('2026-05-14T12:00:00.000Z').toISOString(); // 3 days ago
      expect(component.timeAgo(t)).toBe('3d ago');
    });
  });

  // ── routeFor() ────────────────────────────────────────────────────────────────

  describe('routeFor()', () => {
    it('routes leave_pending to /leave', () => {
      expect(component.routeFor(makeNotif({ type: 'leave_pending' }))).toBe('/leave');
    });

    it('routes leave_approved to /leave', () => {
      expect(component.routeFor(makeNotif({ type: 'leave_approved' }))).toBe('/leave');
    });

    it('routes leave_rejected to /leave', () => {
      expect(component.routeFor(makeNotif({ type: 'leave_rejected' }))).toBe('/leave');
    });

    it('routes stale_shift_admin to /dashboard', () => {
      expect(component.routeFor(makeNotif({ type: 'stale_shift_admin' }))).toBe('/dashboard');
    });

    it('routes roster_assigned to /dashboard', () => {
      expect(component.routeFor(makeNotif({ type: 'roster_assigned' }))).toBe('/dashboard');
    });

    it('routes missed_clockin to /dashboard', () => {
      expect(component.routeFor(makeNotif({ type: 'missed_clockin' }))).toBe('/dashboard');
    });

    it('routes password_reset to /staff', () => {
      expect(component.routeFor(makeNotif({ type: 'password_reset' }))).toBe('/staff');
    });

    it('routes announcement to /announcements', () => {
      expect(component.routeFor(makeNotif({ type: 'announcement' }))).toBe('/announcements');
    });

    it('routes message to /messaging', () => {
      expect(component.routeFor(makeNotif({ type: 'message' }))).toBe('/messaging');
    });

    it('returns null for unknown type', () => {
      expect(component.routeFor(makeNotif({ type: 'unknown_type' as any }))).toBeNull();
    });
  });

  // ── filtered() computed ───────────────────────────────────────────────────────

  describe('filtered() computed', () => {
    it('returns all notifications when filter is "all"', () => {
      const notifs = [makeNotif({ isRead: true }), makeNotif({ id: 'n2', isRead: false })];
      mockNotifSvc.notifications.mockReturnValue(notifs);
      component = TestBed.createComponent(NotificationsComponent).componentInstance;
      component.filter.set('all');
      expect(component.filtered()).toHaveLength(2);
    });

    it('returns only unread notifications when filter is "unread"', () => {
      const notifs = [makeNotif({ isRead: true }), makeNotif({ id: 'n2', isRead: false })];
      mockNotifSvc.notifications.mockReturnValue(notifs);
      component = TestBed.createComponent(NotificationsComponent).componentInstance;
      component.filter.set('unread');
      expect(component.filtered()).toHaveLength(1);
      expect(component.filtered()[0].isRead).toBe(false);
    });
  });

  // ── grouped() computed ────────────────────────────────────────────────────────

  describe('grouped() computed', () => {
    beforeEach(() => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-05-17T12:00:00.000Z'));
    });

    it('places a notification from today in the "Today" group', () => {
      const notifs = [makeNotif({ id: 'n1', createdAt: '2026-05-17T10:00:00.000Z' })];
      mockNotifSvc.notifications.mockReturnValue(notifs);
      component = TestBed.createComponent(NotificationsComponent).componentInstance;
      const groups = component.grouped();
      expect(groups[0]?.label).toBe('Today');
    });

    it('places a notification from yesterday in the "Yesterday" group', () => {
      const notifs = [makeNotif({ id: 'n1', createdAt: '2026-05-16T10:00:00.000Z' })];
      mockNotifSvc.notifications.mockReturnValue(notifs);
      component = TestBed.createComponent(NotificationsComponent).componentInstance;
      const groups = component.grouped();
      expect(groups[0]?.label).toBe('Yesterday');
    });

    it('places an older notification in the "Earlier" group', () => {
      const notifs = [makeNotif({ id: 'n1', createdAt: '2026-05-10T10:00:00.000Z' })];
      mockNotifSvc.notifications.mockReturnValue(notifs);
      component = TestBed.createComponent(NotificationsComponent).componentInstance;
      const groups = component.grouped();
      expect(groups[0]?.label).toBe('Earlier');
    });

    it('produces three groups when notifications span all three periods', () => {
      const notifs = [
        makeNotif({ id: 'n1', createdAt: '2026-05-17T10:00:00.000Z' }),
        makeNotif({ id: 'n2', createdAt: '2026-05-16T10:00:00.000Z' }),
        makeNotif({ id: 'n3', createdAt: '2026-05-10T10:00:00.000Z' }),
      ];
      mockNotifSvc.notifications.mockReturnValue(notifs);
      component = TestBed.createComponent(NotificationsComponent).componentInstance;
      expect(component.grouped()).toHaveLength(3);
    });

    it('returns empty groups array when there are no notifications', () => {
      mockNotifSvc.notifications.mockReturnValue([]);
      component = TestBed.createComponent(NotificationsComponent).componentInstance;
      expect(component.grouped()).toHaveLength(0);
    });
  });

  // ── onNotifClick() ────────────────────────────────────────────────────────────

  describe('onNotifClick()', () => {
    it('calls markReadOptimistic for an unread notification', () => {
      const n = makeNotif({ isRead: false });
      component.onNotifClick(n);
      expect(mockNotifSvc.markReadOptimistic).toHaveBeenCalledWith('n1');
    });

    it('calls markRead for an unread notification', () => {
      const n = makeNotif({ isRead: false });
      component.onNotifClick(n);
      expect(mockNotifSvc.markRead).toHaveBeenCalledWith('n1');
    });

    it('does NOT call markReadOptimistic for an already-read notification', () => {
      const n = makeNotif({ isRead: true });
      component.onNotifClick(n);
      expect(mockNotifSvc.markReadOptimistic).not.toHaveBeenCalled();
    });

    it('reloads on markRead error to revert optimistic update', () => {
      mockNotifSvc.markRead.mockReturnValue(throwError(() => new Error('Network')));
      component.onNotifClick(makeNotif({ isRead: false }));
      expect(mockNotifSvc.load).toHaveBeenCalled();
    });
  });

  // ── markAll() ────────────────────────────────────────────────────────────────

  describe('markAll()', () => {
    it('calls markAllRead on the service', () => {
      component.markAll();
      expect(mockNotifSvc.markAllRead).toHaveBeenCalled();
    });

    it('clears markingAll after success', () => {
      component.markAll();
      expect(component.markingAll()).toBe(false);
    });

    it('reloads on markAllRead error to restore state', () => {
      mockNotifSvc.markAllRead.mockReturnValue(throwError(() => new Error('Network')));
      component.markAll();
      expect(mockNotifSvc.load).toHaveBeenCalled();
    });
  });
});
