import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { HomeComponent } from './home.component';
import {
  AuthService, PayrollService, LeaveService, WorkLogService,
  FeatureFlagService, AnnouncementService, WorkLogDto,
} from 'shared-lib';

/**
 * HomeComponent — staff dashboard landing page.
 *
 * WHY these tests matter:
 *  - The greeting is the first thing staff see — wrong time-of-day message undermines trust.
 *  - showPay and showAnnouncements gate entire card sections; an off flag disables revenue-critical info.
 *  - ngOnInit fans out to 5 APIs via forkJoin. Any signal left empty or set to wrong data
 *    results in a blank dashboard without a visible error.
 *  - catchError on each branch means a single API failure must NOT prevent the rest
 *    of the dashboard from loading.
 */

// ── Shared helpers ────────────────────────────────────────────────────────────

function makeLog(overrides: Partial<WorkLogDto> = {}): WorkLogDto {
  return {
    id: 'log-1', roleName: 'bartender', locationName: 'Main Bar',
    workDate: '2026-05-17', startTime: '18:00', endTime: '23:00',
    hours: 5, cost: 75, isActive: false, isManualEntry: false, notes: null,
    ...overrides,
  };
}

const fakeBalance = {
  year: 2026, annualEntitlement: 14, annualUsed: 2, annualRemaining: 12,
  sickEntitlement: 14, sickUsed: 0, sickRemaining: 14, unpaidUsed: 0,
};

const fakePaySummary = {
  employeeId: 'e1', periodStart: '2026-05-01', periodEnd: '2026-05-31',
  totalHours: 40, regularHours: 40, totalPay: 600,
};

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('HomeComponent', () => {
  let mockPayrollSvc:      { getMySummary: ReturnType<typeof vi.fn> };
  let mockLeaveSvc:        { getMyBalance: ReturnType<typeof vi.fn> };
  let mockWorkLogSvc:      { getActive: ReturnType<typeof vi.fn>; getMy: ReturnType<typeof vi.fn> };
  let mockFeatureSvc:      { isEnabled: ReturnType<typeof vi.fn> };
  let mockAnnouncementSvc: { getMine: ReturnType<typeof vi.fn> };
  let mockAuth:            { user: () => any };

  /**
   * Create (and optionally configure) a fresh component instance.
   * `flags` controls what isEnabled() returns before the computed is first evaluated.
   */
  function createComponent(flags: { paySummary?: boolean; announcements?: boolean } = {}): HomeComponent {
    mockFeatureSvc.isEnabled.mockImplementation(
      (key: string) => (flags as Record<string, boolean>)[key] ?? false,
    );
    return TestBed.createComponent(HomeComponent).componentInstance;
  }

  beforeEach(async () => {
    mockPayrollSvc      = { getMySummary: vi.fn().mockReturnValue(of(fakePaySummary)) };
    mockLeaveSvc        = { getMyBalance: vi.fn().mockReturnValue(of(fakeBalance)) };
    mockWorkLogSvc      = {
      getActive: vi.fn().mockReturnValue(of(null)),
      getMy:     vi.fn().mockReturnValue(of({ items: [], total: 0 })),
    };
    mockFeatureSvc      = { isEnabled: vi.fn().mockReturnValue(false) };
    mockAnnouncementSvc = { getMine: vi.fn().mockReturnValue(of([])) };
    mockAuth            = { user: () => ({ id: 'u1', staffId: 'NC001', fullName: 'Test User', email: 't@test.com', roles: ['employee'] }) };

    await TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AuthService,          useValue: mockAuth },
        { provide: PayrollService,       useValue: mockPayrollSvc },
        { provide: LeaveService,         useValue: mockLeaveSvc },
        { provide: WorkLogService,       useValue: mockWorkLogSvc },
        { provide: FeatureFlagService,   useValue: mockFeatureSvc },
        { provide: AnnouncementService,  useValue: mockAnnouncementSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  // ── greeting computed ─────────────────────────────────────────────────────────

  describe('greeting computed', () => {
    it('returns goodMorning key when the hour is before noon', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17, 9, 0, 0)); // 9am
      expect(createComponent().greeting()).toBe('home.goodMorning');
    });

    it('returns goodAfternoon key between noon (inclusive) and 5pm', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17, 14, 0, 0)); // 2pm
      expect(createComponent().greeting()).toBe('home.goodAfternoon');
    });

    it('returns goodEvening key at 5pm or later', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17, 20, 0, 0)); // 8pm
      expect(createComponent().greeting()).toBe('home.goodEvening');
    });

    it('uses hour 17 as the evening boundary', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17, 17, 0, 0)); // exactly 5pm
      expect(createComponent().greeting()).toBe('home.goodEvening');
    });
  });

  // ── firstName computed ────────────────────────────────────────────────────────

  describe('firstName computed', () => {
    it('extracts the first word of fullName', () => {
      mockAuth.user = () => ({ fullName: 'John Doe', roles: [] });
      expect(createComponent().firstName()).toBe('John');
    });

    it('returns the whole name when it has no spaces', () => {
      mockAuth.user = () => ({ fullName: 'Mononym', roles: [] });
      expect(createComponent().firstName()).toBe('Mononym');
    });

    it('returns empty string when user is null (logged-out race condition)', () => {
      mockAuth.user = () => null;
      expect(createComponent().firstName()).toBe('');
    });
  });

  // ── showPay / showAnnouncements ───────────────────────────────────────────────

  describe('showPay and showAnnouncements feature flag gates', () => {
    it('showPay is false when paySummary flag is disabled', () => {
      const c = createComponent({ paySummary: false });
      expect(c.showPay()).toBe(false);
    });

    it('showPay is true when paySummary flag is enabled', () => {
      const c = createComponent({ paySummary: true });
      expect(c.showPay()).toBe(true);
    });

    it('showAnnouncements is false when announcements flag is disabled', () => {
      const c = createComponent({ announcements: false });
      expect(c.showAnnouncements()).toBe(false);
    });

    it('showAnnouncements is true when announcements flag is enabled', () => {
      const c = createComponent({ announcements: true });
      expect(c.showAnnouncements()).toBe(true);
    });
  });

  // ── ngOnInit() — data loading ─────────────────────────────────────────────────

  describe('ngOnInit() — data population', () => {
    it('starts in loading state before data arrives', () => {
      const c = createComponent();
      // loading = true by default (set in class initializer, before ngOnInit runs)
      expect(c.loading()).toBe(true);
    });

    it('sets loading to false after all API calls complete', () => {
      const c = createComponent();
      c.ngOnInit();
      expect(c.loading()).toBe(false);
    });

    it('populates leave balance signal', () => {
      const c = createComponent();
      c.ngOnInit();
      expect(c.leave()).toEqual(fakeBalance);
    });

    it('populates activeShift when an active log is returned', () => {
      const active = makeLog({ isActive: true, endTime: null });
      mockWorkLogSvc.getActive.mockReturnValue(of(active));
      const c = createComponent();
      c.ngOnInit();
      expect(c.activeShift()).toEqual(active);
    });

    it('sets activeShift to null when no active shift exists', () => {
      mockWorkLogSvc.getActive.mockReturnValue(of(null));
      const c = createComponent();
      c.ngOnInit();
      expect(c.activeShift()).toBeNull();
    });

    it('filters out active shift entries from recentShifts list', () => {
      const active    = makeLog({ id: 'a', isActive: true });
      const completed = makeLog({ id: 'b', isActive: false });
      mockWorkLogSvc.getMy.mockReturnValue(of({ items: [active, completed], total: 2 }));
      const c = createComponent();
      c.ngOnInit();
      expect(c.recentShifts().map(s => s.id)).toEqual(['b']);
    });

    it('caps recentShifts to 4 entries even when more are returned', () => {
      const five = Array.from({ length: 5 }, (_, i) =>
        makeLog({ id: `log-${i}`, isActive: false }),
      );
      mockWorkLogSvc.getMy.mockReturnValue(of({ items: five, total: 5 }));
      const c = createComponent();
      c.ngOnInit();
      expect(c.recentShifts().length).toBe(4);
    });

    it('counts unread announcements when announcements feature is enabled', () => {
      const notices = [
        { id: 'a1', title: 'T', body: 'B', createdByName: 'M', createdAt: '', expiresAt: null, isRead: false },
        { id: 'a2', title: 'T', body: 'B', createdByName: 'M', createdAt: '', expiresAt: null, isRead: true  },
        { id: 'a3', title: 'T', body: 'B', createdByName: 'M', createdAt: '', expiresAt: null, isRead: false },
      ];
      mockAnnouncementSvc.getMine.mockReturnValue(of(notices));
      const c = createComponent({ announcements: true });
      c.ngOnInit();
      expect(c.unreadNotices()).toBe(2);
    });

    it('unreadNotices is 0 when announcements feature is disabled — getMine() is not called', () => {
      const c = createComponent({ announcements: false });
      c.ngOnInit();
      expect(c.unreadNotices()).toBe(0);
      expect(mockAnnouncementSvc.getMine).not.toHaveBeenCalled();
    });
  });

  // ── ngOnInit() — pay feature flag gate ───────────────────────────────────────

  describe('ngOnInit() — pay feature flag gate', () => {
    it('does not call getMySummary when paySummary flag is disabled', () => {
      const c = createComponent({ paySummary: false });
      c.ngOnInit();
      expect(mockPayrollSvc.getMySummary).not.toHaveBeenCalled();
    });

    it('pay signal remains null when paySummary flag is disabled', () => {
      const c = createComponent({ paySummary: false });
      c.ngOnInit();
      expect(c.pay()).toBeNull();
    });

    it('calls getMySummary and populates pay signal when paySummary is enabled', () => {
      const c = createComponent({ paySummary: true });
      c.ngOnInit();
      expect(mockPayrollSvc.getMySummary).toHaveBeenCalled();
      expect(c.pay()).toEqual(fakePaySummary);
    });
  });

  // ── ngOnInit() — graceful error handling ─────────────────────────────────────

  describe('ngOnInit() — graceful error handling', () => {
    it('still loads (loading=false) when leave balance API fails', () => {
      mockLeaveSvc.getMyBalance.mockReturnValue(throwError(() => new Error('Network')));
      const c = createComponent();
      c.ngOnInit();
      expect(c.loading()).toBe(false);
    });

    it('sets leave to null when the leave balance API fails', () => {
      mockLeaveSvc.getMyBalance.mockReturnValue(throwError(() => new Error('Network')));
      const c = createComponent();
      c.ngOnInit();
      expect(c.leave()).toBeNull();
    });

    it('still loads when active shift API fails', () => {
      mockWorkLogSvc.getActive.mockReturnValue(throwError(() => new Error('Network')));
      const c = createComponent();
      c.ngOnInit();
      expect(c.loading()).toBe(false);
    });

    it('still loads when recent shifts API fails — other data is still shown', () => {
      mockWorkLogSvc.getMy.mockReturnValue(throwError(() => new Error('Network')));
      const c = createComponent();
      c.ngOnInit();
      expect(c.loading()).toBe(false);
    });

    it('sets recentShifts to empty when the API fails', () => {
      mockWorkLogSvc.getMy.mockReturnValue(throwError(() => new Error('Network')));
      const c = createComponent();
      c.ngOnInit();
      expect(c.recentShifts()).toEqual([]);
    });
  });
});
