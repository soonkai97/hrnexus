import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { HomeComponent } from './home.component';
import { AuthService, LeaveService } from 'shared-lib';

/**
 * HomeComponent — staff dashboard landing page.
 *
 * WHY these tests matter:
 *  - The greeting is the first thing staff see — wrong time-of-day message undermines trust.
 *  - ngOnInit loads the leave balance. A failure must NOT leave the page stuck in the
 *    loading skeleton; it must fall back to the "data unavailable" cards.
 */

const fakeBalance = {
  year: 2026, annualEntitlement: 14, annualUsed: 2, annualRemaining: 12,
  sickEntitlement: 14, sickUsed: 0, sickRemaining: 14, unpaidUsed: 0,
};

describe('HomeComponent', () => {
  let mockLeaveSvc: { getMyBalance: ReturnType<typeof vi.fn> };
  let mockAuth:     { user: () => any };

  function createComponent(): HomeComponent {
    return TestBed.createComponent(HomeComponent).componentInstance;
  }

  beforeEach(async () => {
    mockLeaveSvc = { getMyBalance: vi.fn().mockReturnValue(of(fakeBalance)) };
    mockAuth     = { user: () => ({ id: 'u1', staffId: 'NC001', fullName: 'Test User', email: 't@test.com', roles: ['employee'] }) };

    await TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: AuthService,  useValue: mockAuth },
        { provide: LeaveService, useValue: mockLeaveSvc },
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
      expect(createComponent().greeting()).toBe('Good morning');
    });

    it('returns goodAfternoon key between noon (inclusive) and 5pm', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17, 14, 0, 0)); // 2pm
      expect(createComponent().greeting()).toBe('Good afternoon');
    });

    it('returns goodEvening key at 5pm or later', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17, 20, 0, 0)); // 8pm
      expect(createComponent().greeting()).toBe('Good evening');
    });

    it('uses hour 17 as the evening boundary', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 4, 17, 17, 0, 0)); // exactly 5pm
      expect(createComponent().greeting()).toBe('Good evening');
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

  // ── ngOnInit() — data loading ─────────────────────────────────────────────────

  describe('ngOnInit() — data population', () => {
    it('starts in loading state before data arrives', () => {
      expect(createComponent().loading()).toBe(true);
    });

    it('sets loading to false after the leave balance loads', () => {
      const c = createComponent();
      c.ngOnInit();
      expect(c.loading()).toBe(false);
    });

    it('populates leave balance signal', () => {
      const c = createComponent();
      c.ngOnInit();
      expect(c.leave()).toEqual(fakeBalance);
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
  });
});
