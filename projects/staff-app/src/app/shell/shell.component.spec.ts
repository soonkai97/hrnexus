import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ShellComponent } from './shell.component';
import { AuthService, LangService, FeatureFlagService, SignalRService, NotificationService, AnnouncementService } from 'shared-lib';

/**
 * Staff ShellComponent — layout shell with bottom navigation bar.
 *
 * WHY these tests matter:
 *  - navItems computed filters feature-flag-gated routes — if the filter is broken,
 *    disabled features (e.g. pay-summary) appear in the nav before they are enabled.
 *  - initials computed drives the staff avatar in the top bar; empty or wrong initials
 *    are visible on every page.
 *  - userRoleLabel must reflect the correct role for the top-bar label.
 *  - toggleSidebar() must toggle collapsed state for mobile drawer behaviour.
 *  - logout() must stop SignalR before logging out to avoid dangling connections.
 *  - refreshUnread() must only call announcementSvc.getMine() when announcements
 *    flag is enabled — calling it while disabled wastes a network request.
 */

describe('ShellComponent (staff-app)', () => {
  let mockAuth:            { user: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };
  let mockLang:            { current: ReturnType<typeof vi.fn>; set: ReturnType<typeof vi.fn>; syncFromApi: ReturnType<typeof vi.fn> };
  let mockFeature:         { load: ReturnType<typeof vi.fn>; flags: ReturnType<typeof vi.fn>; isEnabled: ReturnType<typeof vi.fn> };
  let mockSignalR:         { startConnection: ReturnType<typeof vi.fn>; stopConnection: ReturnType<typeof vi.fn> };
  let mockNotifSvc:        { unreadCount: ReturnType<typeof vi.fn> };
  let mockAnnouncementSvc: { getMine: ReturnType<typeof vi.fn> };

  function createComponent(userRoles: string[] = ['employee'], fullName = 'Jane Doe') {
    mockAuth.user.mockReturnValue({ fullName, roles: userRoles });
    return TestBed.createComponent(ShellComponent).componentInstance;
  }

  beforeEach(async () => {
    mockAuth            = { user: vi.fn().mockReturnValue(null), logout: vi.fn() };
    mockLang            = { current: vi.fn().mockReturnValue('en'), set: vi.fn(), syncFromApi: vi.fn() };
    mockFeature         = {
      load:      vi.fn().mockReturnValue(of(undefined)),
      flags:     vi.fn().mockReturnValue({
        payroll: false, tips: false, tipsHistory: false,
        paySummary: false, announcements: false, staffLedger: false,
      }),
      isEnabled: vi.fn().mockReturnValue(false),
    };
    mockSignalR         = { startConnection: vi.fn(), stopConnection: vi.fn() };
    mockNotifSvc        = { unreadCount: vi.fn().mockReturnValue(0) };
    mockAnnouncementSvc = { getMine: vi.fn().mockReturnValue(of([])) };

    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AuthService,          useValue: mockAuth },
        { provide: LangService,          useValue: mockLang },
        { provide: FeatureFlagService,   useValue: mockFeature },
        { provide: SignalRService,       useValue: mockSignalR },
        { provide: NotificationService,  useValue: mockNotifSvc },
        { provide: AnnouncementService,  useValue: mockAnnouncementSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  // ── initials computed ─────────────────────────────────────────────────────────

  describe('initials computed', () => {
    it('returns two uppercase initials for a full name', () => {
      const c = createComponent();
      expect(c.initials()).toBe('JD');
    });

    it('returns empty string when user is null', () => {
      mockAuth.user.mockReturnValue(null);
      const c = TestBed.createComponent(ShellComponent).componentInstance;
      expect(c.initials()).toBe('');
    });

    it('caps initials at 2 characters for multi-word names', () => {
      const c = createComponent(['employee'], 'Alice Mary Smith');
      expect(c.initials()).toBe('AM');
    });
  });

  // ── userRoleLabel computed ────────────────────────────────────────────────────

  describe('userRoleLabel computed', () => {
    it('returns "Manager" for manager role', () => {
      const c = createComponent(['manager']);
      expect(c.userRoleLabel()).toBe('Manager');
    });

    it('returns "Admin" for admin role', () => {
      const c = createComponent(['admin']);
      expect(c.userRoleLabel()).toBe('Admin');
    });

    it('returns title-cased first role for other roles', () => {
      const c = createComponent(['employee']);
      expect(c.userRoleLabel()).toBe('Employee');
    });

    it('returns "Staff" when user has no roles', () => {
      mockAuth.user.mockReturnValue({ fullName: 'No Roles', roles: [] });
      const c = TestBed.createComponent(ShellComponent).componentInstance;
      expect(c.userRoleLabel()).toBe('Staff');
    });
  });

  // ── navItems computed ─────────────────────────────────────────────────────────

  describe('navItems computed', () => {
    it('always includes /home and /log-shift', () => {
      const c = createComponent();
      const paths = c.navItems().map((n: any) => n.path);
      expect(paths).toContain('/home');
      expect(paths).toContain('/log-shift');
    });

    it('excludes /pay-summary when paySummary flag is disabled', () => {
      const c = createComponent();
      const paths = c.navItems().map((n: any) => n.path);
      expect(paths).not.toContain('/pay-summary');
    });

    it('includes /pay-summary when paySummary flag is enabled', () => {
      mockFeature.flags.mockReturnValue({
        payroll: false, tips: false, tipsHistory: false,
        paySummary: true, announcements: false, staffLedger: false,
      });
      const c = createComponent();
      const paths = c.navItems().map((n: any) => n.path);
      expect(paths).toContain('/pay-summary');
    });

    it('excludes /tips-history when tipsHistory flag is disabled', () => {
      const c = createComponent();
      const paths = c.navItems().map((n: any) => n.path);
      expect(paths).not.toContain('/tips-history');
    });

    it('excludes /announcements when announcements flag is disabled', () => {
      const c = createComponent();
      const paths = c.navItems().map((n: any) => n.path);
      expect(paths).not.toContain('/announcements');
    });
  });

  // ── toggleSidebar() ───────────────────────────────────────────────────────────

  describe('toggleSidebar()', () => {
    it('toggles collapsed state', () => {
      const c = createComponent();
      const initial = c.collapsed();
      c.toggleSidebar();
      expect(c.collapsed()).toBe(!initial);
    });

    it('restores collapsed state when toggled twice', () => {
      const c = createComponent();
      const initial = c.collapsed();
      c.toggleSidebar();
      c.toggleSidebar();
      expect(c.collapsed()).toBe(initial);
    });
  });

  // ── logout() ─────────────────────────────────────────────────────────────────

  describe('logout()', () => {
    it('stops SignalR before logging out', () => {
      const c = createComponent();
      c.logout();
      expect(mockSignalR.stopConnection).toHaveBeenCalled();
    });

    it('calls auth.logout()', () => {
      const c = createComponent();
      c.logout();
      expect(mockAuth.logout).toHaveBeenCalled();
    });
  });

  // ── constructor side effects ──────────────────────────────────────────────────

  describe('constructor side effects', () => {
    it('calls featureSvc.load() on construction', () => {
      createComponent();
      expect(mockFeature.load).toHaveBeenCalled();
    });

    it('starts the SignalR connection on construction', () => {
      createComponent();
      expect(mockSignalR.startConnection).toHaveBeenCalled();
    });

    it('does NOT call announcementSvc.getMine when announcements flag is disabled', () => {
      mockFeature.isEnabled.mockReturnValue(false);
      createComponent();
      expect(mockAnnouncementSvc.getMine).not.toHaveBeenCalled();
    });

    it('calls announcementSvc.getMine when announcements flag is enabled', () => {
      mockFeature.isEnabled.mockReturnValue(true);
      mockAnnouncementSvc.getMine.mockReturnValue(of([]));
      createComponent();
      expect(mockAnnouncementSvc.getMine).toHaveBeenCalled();
    });
  });
});
