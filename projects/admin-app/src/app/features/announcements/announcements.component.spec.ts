import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { AnnouncementsComponent } from './announcements.component';
import { AnnouncementService, ConfirmService, ToastService } from 'shared-lib';

/**
 * AnnouncementsComponent — admin announcement CRUD with pagination.
 *
 * WHY these tests matter:
 *  - save() has a 5-layer validation chain — any broken guard lets invalid data
 *    reach the API (e.g., empty title creates a nameless announcement; a past
 *    expiry date causes the announcement to be immediately invisible to staff).
 *  - create vs update path distinction is critical — using update when no editId
 *    is set would PUT to /announcements/undefined, returning a 400.
 *  - totalPages computed drives the paginator; wrong ceiling math shows a page
 *    that doesn't exist or hides the last page.
 */

function makeItem(overrides: Partial<any> = {}): any {
  return {
    id: 'a1', title: 'Test', body: 'Body', targetRoles: '*',
    isActive: true, createdByName: 'Admin', createdAt: '2026-05-17T10:00:00Z',
    expiresAt: null, readCount: 0,
    ...overrides,
  };
}

describe('AnnouncementsComponent', () => {
  let component: AnnouncementsComponent;

  let mockAnnouncementSvc: {
    getAll:   ReturnType<typeof vi.fn>;
    create:   ReturnType<typeof vi.fn>;
    update:   ReturnType<typeof vi.fn>;
    delete:   ReturnType<typeof vi.fn>;
  };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAnnouncementSvc = {
      getAll:  vi.fn().mockReturnValue(of({ items: [], total: 0 })),
      create:  vi.fn().mockReturnValue(of({ id: 'new-a' })),
      update:  vi.fn().mockReturnValue(of(undefined)),
      delete:  vi.fn().mockReturnValue(of(undefined)),
    };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [AnnouncementsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AnnouncementService, useValue: mockAnnouncementSvc },
        { provide: ConfirmService,      useValue: mockConfirmSvc },
        { provide: ToastService,        useValue: mockToastSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(AnnouncementsComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('loads the first page of announcements on init', () => {
      component.ngOnInit();
      expect(mockAnnouncementSvc.getAll).toHaveBeenCalledWith(1, 10);
    });

    it('populates items signal with fetched data', () => {
      const items = [makeItem()];
      mockAnnouncementSvc.getAll.mockReturnValue(of({ items, total: 1 }));
      component.ngOnInit();
      expect(component.items()).toEqual(items);
    });

    it('sets totalCount signal with the API total', () => {
      mockAnnouncementSvc.getAll.mockReturnValue(of({ items: [], total: 42 }));
      component.ngOnInit();
      expect(component.totalCount()).toBe(42);
    });

    it('shows error toast when load fails', () => {
      mockAnnouncementSvc.getAll.mockReturnValue(throwError(() => new Error('Network')));
      component.ngOnInit();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Failed to load announcements.');
    });
  });

  // ── totalPages computed ───────────────────────────────────────────────────────

  describe('totalPages computed', () => {
    it('returns 1 when there are no items', () => {
      component.totalCount.set(0);
      expect(component.totalPages()).toBe(1);
    });

    it('returns 1 for exactly one page of items', () => {
      component.totalCount.set(10);
      expect(component.totalPages()).toBe(1);
    });

    it('returns ceiling of total / pageSize for partial last page', () => {
      component.totalCount.set(11);
      expect(component.totalPages()).toBe(2);
    });

    it('returns correct page count for large total', () => {
      component.totalCount.set(45);
      expect(component.totalPages()).toBe(5);
    });
  });

  // ── save() — validation ───────────────────────────────────────────────────────

  describe('save() — validation', () => {
    it('blocks empty title — a nameless announcement confuses staff', () => {
      component.form.title = '';
      component.form.body  = 'Valid body.';
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Title and body are required.');
      expect(mockAnnouncementSvc.create).not.toHaveBeenCalled();
    });

    it('blocks whitespace-only title', () => {
      component.form.title = '   ';
      component.form.body  = 'Valid body.';
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Title and body are required.');
    });

    it('blocks empty body', () => {
      component.form.title = 'Valid Title';
      component.form.body  = '';
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Title and body are required.');
    });

    it('blocks title exceeding 200 characters', () => {
      component.form.title = 'A'.repeat(201);
      component.form.body  = 'Valid body.';
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Title cannot exceed 200 characters.');
    });

    it('allows title of exactly 200 characters', () => {
      component.form.title = 'A'.repeat(200);
      component.form.body  = 'Valid body.';
      component.save();
      expect(mockAnnouncementSvc.create).toHaveBeenCalled();
    });

    it('blocks body exceeding 5000 characters', () => {
      component.form.title = 'Valid Title';
      component.form.body  = 'B'.repeat(5001);
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Body cannot exceed 5000 characters.');
    });

    it('blocks past expiry date — would make the announcement invisible immediately', () => {
      component.form.title    = 'Valid Title';
      component.form.body     = 'Valid body.';
      component.form.expiresAt = '2020-01-01'; // past date
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Expiry date must be in the future.');
    });

    it('allows a future expiry date', () => {
      component.form.title    = 'Valid Title';
      component.form.body     = 'Valid body.';
      component.form.expiresAt = '2099-12-31';
      component.save();
      expect(mockAnnouncementSvc.create).toHaveBeenCalled();
    });

    it('allows empty expiresAt — no expiry is valid', () => {
      component.form.title    = 'Valid Title';
      component.form.body     = 'Valid body.';
      component.form.expiresAt = '';
      component.save();
      expect(mockAnnouncementSvc.create).toHaveBeenCalled();
    });
  });

  // ── save() — create path ──────────────────────────────────────────────────────

  describe('save() — create path (editing is null)', () => {
    beforeEach(() => {
      component.form = { title: 'Hello', body: 'World', targetRoles: [], expiresAt: '' };
      component.editing.set(null);
    });

    it('calls create (not update) when not editing', () => {
      component.save();
      expect(mockAnnouncementSvc.create).toHaveBeenCalled();
      expect(mockAnnouncementSvc.update).not.toHaveBeenCalled();
    });

    it('shows "Announcement created." toast on success', () => {
      component.save();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Announcement created.');
    });

    it('hides the form after creating', () => {
      component.showForm.set(true);
      component.save();
      expect(component.showForm()).toBe(false);
    });

    it('shows "Save failed." toast on create error', () => {
      mockAnnouncementSvc.create.mockReturnValue(throwError(() => new Error('Network')));
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Save failed.');
      expect(component.saving()).toBe(false);
    });
  });

  // ── save() — update path ──────────────────────────────────────────────────────

  describe('save() — update path (editing is set)', () => {
    beforeEach(() => {
      component.form = { title: 'Hello', body: 'World', targetRoles: [], expiresAt: '' };
      component.editing.set('a1');
    });

    it('calls update (not create) when editing is set', () => {
      component.save();
      expect(mockAnnouncementSvc.update).toHaveBeenCalledWith('a1', expect.any(Object));
      expect(mockAnnouncementSvc.create).not.toHaveBeenCalled();
    });

    it('shows "Announcement updated." toast on success', () => {
      component.save();
      expect(mockToastSvc.success).toHaveBeenCalledWith('Announcement updated.');
    });
  });

  // ── startEdit() ───────────────────────────────────────────────────────────────

  describe('startEdit()', () => {
    it('populates form fields from the announcement', () => {
      component.startEdit(makeItem({ title: 'News', body: 'Details', targetRoles: 'manager' }));
      expect(component.form.title).toBe('News');
      expect(component.form.body).toBe('Details');
      expect(component.form.targetRoles).toEqual(['manager']);
    });

    it('converts * targetRoles to empty array (meaning all roles)', () => {
      component.startEdit(makeItem({ targetRoles: '*' }));
      expect(component.form.targetRoles).toEqual([]);
    });

    it('splits comma-separated targetRoles into an array', () => {
      component.startEdit(makeItem({ targetRoles: 'manager,employee' }));
      expect(component.form.targetRoles).toEqual(['manager', 'employee']);
    });

    it('slices expiresAt to date-only (first 10 chars)', () => {
      component.startEdit(makeItem({ expiresAt: '2026-12-31T00:00:00Z' }));
      expect(component.form.expiresAt).toBe('2026-12-31');
    });

    it('sets expiresAt to empty string when expiresAt is null', () => {
      component.startEdit(makeItem({ expiresAt: null }));
      expect(component.form.expiresAt).toBe('');
    });

    it('sets editing signal to the announcement id', () => {
      component.startEdit(makeItem({ id: 'a99' }));
      expect(component.editing()).toBe('a99');
    });

    it('shows the form panel', () => {
      component.startEdit(makeItem());
      expect(component.showForm()).toBe(true);
    });
  });

  // ── remove() ─────────────────────────────────────────────────────────────────

  describe('remove()', () => {
    it('shows a confirmation dialog before deleting', () => {
      component.remove('a1');
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('calls delete when confirmed', () => {
      component.remove('a1');
      expect(mockAnnouncementSvc.delete).toHaveBeenCalledWith('a1');
    });

    it('shows success toast after deletion', () => {
      component.remove('a1');
      expect(mockToastSvc.success).toHaveBeenCalledWith('Deleted.');
    });

    it('reloads announcements after deletion', () => {
      mockAnnouncementSvc.getAll.mockClear();
      component.remove('a1');
      expect(mockAnnouncementSvc.getAll).toHaveBeenCalled();
    });

    it('does NOT delete when confirmation is cancelled', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.remove('a1');
      expect(mockAnnouncementSvc.delete).not.toHaveBeenCalled();
    });

    it('shows error toast when deletion fails', () => {
      mockAnnouncementSvc.delete.mockReturnValue(throwError(() => new Error('Network')));
      component.remove('a1');
      expect(mockToastSvc.error).toHaveBeenCalledWith('Delete failed.');
    });
  });
});
