import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { AnnouncementsComponent } from './announcements.component';
import { AnnouncementService, ToastService } from 'shared-lib';

/**
 * AnnouncementsComponent (staff) — staff announcement feed with optimistic read marks.
 *
 * WHY these tests matter:
 *  - markRead() applies an optimistic UI update immediately so the unread badge
 *    clears before the server responds — any failure to revert on error leaves
 *    the item permanently "read" in the UI even though the server disagrees.
 *  - markAllRead() reverts individual items on their API error — a partial revert
 *    is critical because only some markRead calls may fail.
 *  - unreadCount / hasUnread drive the red badge on the shell nav; wrong counts
 *    would show a stale badge even when all announcements are read.
 */

function makeAnnouncement(overrides: Partial<any> = {}): any {
  return {
    id: 'a1', title: 'Important notice', body: 'Details here.',
    createdByName: 'Admin', createdAt: '2026-05-17T10:00:00Z',
    expiresAt: null, isRead: false,
    ...overrides,
  };
}

describe('AnnouncementsComponent (staff)', () => {
  let component: AnnouncementsComponent;

  let mockAnnouncementSvc: {
    getMine:  ReturnType<typeof vi.fn>;
    markRead: ReturnType<typeof vi.fn>;
  };
  let mockToastSvc: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAnnouncementSvc = {
      getMine:  vi.fn().mockReturnValue(of([])),
      markRead: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToastSvc = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [AnnouncementsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AnnouncementService, useValue: mockAnnouncementSvc },
        { provide: ToastService,        useValue: mockToastSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(AnnouncementsComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() / load() ───────────────────────────────────────────────────────

  describe('ngOnInit() / load()', () => {
    it('fetches announcements on init', () => {
      component.ngOnInit();
      expect(mockAnnouncementSvc.getMine).toHaveBeenCalled();
    });

    it('populates items signal with the fetched list', () => {
      const items = [makeAnnouncement()];
      mockAnnouncementSvc.getMine.mockReturnValue(of(items));
      component.ngOnInit();
      expect(component.items()).toEqual(items);
    });

    it('clears loading flag after successful load', () => {
      component.ngOnInit();
      expect(component.loading()).toBe(false);
    });

    it('shows error toast and clears loading on failure', () => {
      mockAnnouncementSvc.getMine.mockReturnValue(throwError(() => new Error('Network')));
      component.ngOnInit();
      expect(mockToastSvc.error).toHaveBeenCalledWith('announcements.errLoad');
      expect(component.loading()).toBe(false);
    });
  });

  // ── unreadCount / hasUnread computed ──────────────────────────────────────────

  describe('unreadCount / hasUnread computed', () => {
    it('unreadCount returns the number of unread items', () => {
      component.items.set([
        makeAnnouncement({ isRead: false }),
        makeAnnouncement({ id: 'a2', isRead: true }),
        makeAnnouncement({ id: 'a3', isRead: false }),
      ]);
      expect(component.unreadCount()).toBe(2);
    });

    it('unreadCount returns 0 when all items are read', () => {
      component.items.set([makeAnnouncement({ isRead: true })]);
      expect(component.unreadCount()).toBe(0);
    });

    it('hasUnread returns true when there are unread items', () => {
      component.items.set([makeAnnouncement({ isRead: false })]);
      expect(component.hasUnread()).toBe(true);
    });

    it('hasUnread returns false when all items are read', () => {
      component.items.set([makeAnnouncement({ isRead: true })]);
      expect(component.hasUnread()).toBe(false);
    });

    it('hasUnread returns false when items list is empty', () => {
      component.items.set([]);
      expect(component.hasUnread()).toBe(false);
    });
  });

  // ── markRead() ────────────────────────────────────────────────────────────────

  describe('markRead()', () => {
    it('does nothing when the announcement is already read', () => {
      component.markRead(makeAnnouncement({ isRead: true }));
      expect(mockAnnouncementSvc.markRead).not.toHaveBeenCalled();
    });

    it('optimistically marks the item as read before the API responds', () => {
      component.items.set([makeAnnouncement({ isRead: false })]);
      component.markRead(component.items()[0]);
      expect(component.items()[0].isRead).toBe(true);
    });

    it('calls markRead on the service', () => {
      component.items.set([makeAnnouncement({ isRead: false })]);
      component.markRead(component.items()[0]);
      expect(mockAnnouncementSvc.markRead).toHaveBeenCalledWith('a1');
    });

    it('reverts the optimistic update on API error — user can retry', () => {
      component.items.set([makeAnnouncement({ isRead: false })]);
      mockAnnouncementSvc.markRead.mockReturnValue(throwError(() => new Error('Network')));
      component.markRead(component.items()[0]);
      expect(component.items()[0].isRead).toBe(false);
    });

    it('shows error toast on API error', () => {
      component.items.set([makeAnnouncement({ isRead: false })]);
      mockAnnouncementSvc.markRead.mockReturnValue(throwError(() => new Error('Network')));
      component.markRead(component.items()[0]);
      expect(mockToastSvc.error).toHaveBeenCalledWith('announcements.errMarkRead');
    });
  });

  // ── markAllRead() ─────────────────────────────────────────────────────────────

  describe('markAllRead()', () => {
    it('optimistically marks all unread items as read', () => {
      component.items.set([
        makeAnnouncement({ id: 'a1', isRead: false }),
        makeAnnouncement({ id: 'a2', isRead: false }),
      ]);
      component.markAllRead();
      expect(component.items().every(a => a.isRead)).toBe(true);
    });

    it('calls markRead for each previously unread item', () => {
      component.items.set([
        makeAnnouncement({ id: 'a1', isRead: false }),
        makeAnnouncement({ id: 'a2', isRead: false }),
        makeAnnouncement({ id: 'a3', isRead: true }),
      ]);
      component.markAllRead();
      // Only a1 and a2 were unread — markRead called twice (a3 was already read)
      expect(mockAnnouncementSvc.markRead).toHaveBeenCalledTimes(2);
    });

    it('reverts only the failed item when one markRead call fails', () => {
      const items = [
        makeAnnouncement({ id: 'a1', isRead: false }),
        makeAnnouncement({ id: 'a2', isRead: false }),
      ];
      component.items.set(items);
      mockAnnouncementSvc.markRead
        .mockReturnValueOnce(of(undefined))              // a1 succeeds
        .mockReturnValueOnce(throwError(() => new Error('Network'))); // a2 fails
      component.markAllRead();
      const a1 = component.items().find(a => a.id === 'a1')!;
      const a2 = component.items().find(a => a.id === 'a2')!;
      expect(a1.isRead).toBe(true);  // stayed read
      expect(a2.isRead).toBe(false); // reverted
    });

    it('shows success toast after marking all read', () => {
      component.items.set([makeAnnouncement({ isRead: false })]);
      component.markAllRead();
      expect(mockToastSvc.success).toHaveBeenCalled();
    });
  });
});
