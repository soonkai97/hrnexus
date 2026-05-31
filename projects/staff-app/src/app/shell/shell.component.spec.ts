import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ShellComponent } from './shell.component';
import { AuthService, LangService, FeatureFlagService, SignalRService, NotificationService, AppUpdateService } from 'shared-lib';

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
 */

describe('ShellComponent (staff-app)', () => {
  let mockAuth:            { user: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };
  let mockLang:            { current: ReturnType<typeof vi.fn>; set: ReturnType<typeof vi.fn>; syncFromApi: ReturnType<typeof vi.fn> };
  let mockFeature:         { load: ReturnType<typeof vi.fn>; flags: ReturnType<typeof vi.fn>; isEnabled: ReturnType<typeof vi.fn> };
  let mockSignalR:         { startConnection: ReturnType<typeof vi.fn>; stopConnection: ReturnType<typeof vi.fn>; notifications$: unknown };
  let mockNotifSvc:        { notifications: ReturnType<typeof vi.fn>; unreadCount: ReturnType<typeof vi.fn>; load: ReturnType<typeof vi.fn>; push: ReturnType<typeof vi.fn> };
  let mockAppUpdate:       { init: ReturnType<typeof vi.fn> };

  function createComponent(userRoles: string[] = ['BARTENDER'], fullName = 'Jane Doe') {
    mockAuth.user.mockReturnValue({ fullName, roles: userRoles });
    return TestBed.createComponent(ShellComponent).componentInstance;
  }

  beforeEach(async () => {
    mockAuth            = { user: vi.fn().mockReturnValue(null), logout: vi.fn() };
    mockLang            = { current: vi.fn().mockReturnValue('en'), set: vi.fn(), syncFromApi: vi.fn() };
    mockFeature         = {
      load:      vi.fn().mockReturnValue(of(undefined)),
      flags:     vi.fn().mockReturnValue({ payroll: false, paySummary: false }),
      isEnabled: vi.fn().mockReturnValue(false),
    };
    mockSignalR         = { startConnection: vi.fn(), stopConnection: vi.fn(), notifications$: of(null) };
    mockNotifSvc        = { notifications: vi.fn().mockReturnValue([]), unreadCount: vi.fn().mockReturnValue(0), load: vi.fn().mockReturnValue(of(undefined)), push: vi.fn() };
    mockAppUpdate       = { init: vi.fn() };

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
        { provide: AppUpdateService,     useValue: mockAppUpdate },
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
      const c = createComponent(['BARTENDER'], 'Alice Mary Smith');
      expect(c.initials()).toBe('AM');
    });
  });

  // ── userRoleLabel computed ────────────────────────────────────────────────────

  describe('userRoleLabel computed', () => {
    it('returns "Manager" for manager role', () => {
      const c = createComponent(['MANAGER']);
      expect(c.userRoleLabel()).toBe('Manager');
    });

    it('returns "Admin" for admin role', () => {
      const c = createComponent(['SUPERADMIN']);
      expect(c.userRoleLabel()).toBe('Admin');
    });

    it('returns the role name as-is for other roles', () => {
      const c = createComponent(['BARTENDER']);
      expect(c.userRoleLabel()).toBe('BARTENDER');
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
      mockFeature.flags.mockReturnValue({ payroll: false, paySummary: true });
      const c = createComponent();
      const paths = c.navItems().map((n: any) => n.path);
      expect(paths).toContain('/pay-summary');
    });
  });

  // ── avatarMenuItems computed ──────────────────────────────────────────────────

  describe('avatarMenuItems computed', () => {
    it('shows the signed-in user full name as a disabled header item', () => {
      const c = createComponent();
      const header = c.avatarMenuItems()[0];
      expect(header.label).toBe('Jane Doe');
      expect(header.disabled).toBe(true);
    });

    it('includes a Profile item linking to /profile', () => {
      const c = createComponent();
      const profile = c.avatarMenuItems().find((i: any) => i.label === 'nav.profile');
      expect(profile).toBeDefined();
      expect(profile!.routerLink).toBe('/profile');
    });

    it('includes a Sign Out item with a command callback', () => {
      const c = createComponent();
      const signOut = c.avatarMenuItems().find((i: any) => i.label === 'profile.signOut');
      expect(signOut).toBeDefined();
      expect(typeof signOut!.command).toBe('function');
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
    it('shows the signing-out overlay immediately (sync feedback)', async () => {
      const c = createComponent();
      c.logout();
      expect(c.signingOut()).toBe(true);          // set synchronously, before nav
      await new Promise(r => setTimeout(r));        // drain the deferred timer
    });

    it('stops SignalR before logging out', async () => {
      const c = createComponent();
      c.logout();
      await new Promise(r => setTimeout(r));   // flush the deferred navigation
      expect(mockSignalR.stopConnection).toHaveBeenCalled();
    });

    it('calls auth.logout()', async () => {
      const c = createComponent();
      c.logout();
      await new Promise(r => setTimeout(r));
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
  });
});
