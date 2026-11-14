import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ShellComponent } from './shell.component';
import { AuthService, SignalRService, NotificationService, AppUpdateService, PermissionService, CompanyPolicyService, LeaveService, ToastService } from 'shared-lib';

/**
 * ShellComponent: the one layout for every signed-in user.
 *
 * WHY these tests matter:
 *  - The menu is the first thing a person sees. Showing a management link to someone without the
 *    permission is confusing (the server would refuse it), and hiding "My work" would strand staff.
 *  - A decided leave request must reach the person (toast + badge) without them opening My Leave.
 *  - initials drive the avatar badge shown on every page.
 *  - toggleSidebar() must toggle: otherwise the drawer cannot open or close on phones.
 *  - logout() must stop SignalR before calling auth.logout() to avoid zombie connections.
 */

describe('ShellComponent', () => {
  let mockAuth:       { user: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };
  let mockSignalR:    { startConnection: ReturnType<typeof vi.fn>; stopConnection: ReturnType<typeof vi.fn>; notifications$: unknown };
  let mockNotifSvc:   { notifications: ReturnType<typeof vi.fn>; unreadCount: ReturnType<typeof vi.fn>; load: ReturnType<typeof vi.fn>; push: ReturnType<typeof vi.fn> };
  let mockAppUpdate:  { init: ReturnType<typeof vi.fn> };
  let mockLeave:      { getMine: ReturnType<typeof vi.fn> };
  let mockToast:      { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let held: string[];
  let mockPolicyLoad: ReturnType<typeof vi.fn>;

  function createComponent() {
    mockAuth.user.mockReturnValue({ fullName: 'Jane Doe', roles: ['EMPLOYEE'] });
    return TestBed.createComponent(ShellComponent).componentInstance;
  }

  beforeEach(async () => {
    mockAuth      = { user: vi.fn().mockReturnValue(null), logout: vi.fn() };
    mockSignalR   = { startConnection: vi.fn(), stopConnection: vi.fn(), notifications$: of(null) };
    mockNotifSvc  = { notifications: vi.fn().mockReturnValue([]), unreadCount: vi.fn().mockReturnValue(0), load: vi.fn().mockReturnValue(of(undefined)), push: vi.fn() };
    mockAppUpdate = { init: vi.fn() };
    mockLeave     = { getMine: vi.fn().mockReturnValue(of([])) };
    mockToast     = { success: vi.fn(), error: vi.fn() };
    held          = [];
    mockPolicyLoad = vi.fn().mockReturnValue(of(undefined));
    localStorage.clear();

    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: AuthService,         useValue: mockAuth },
        { provide: SignalRService,      useValue: mockSignalR },
        { provide: NotificationService, useValue: mockNotifSvc },
        { provide: AppUpdateService,    useValue: mockAppUpdate },
        { provide: PermissionService,   useValue: { has: (k: string) => held.includes(k), load: () => of(undefined) } },
        { provide: CompanyPolicyService, useValue: { load: mockPolicyLoad } },
        { provide: LeaveService,        useValue: mockLeave },
        { provide: ToastService,        useValue: mockToast },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  // ── initials computed ─────────────────────────────────────────────────────────

  describe('initials computed', () => {
    it('returns two-character uppercase initials for a full name', () => {
      const c = createComponent();
      expect(c.initials()).toBe('JD');
    });

    it('returns empty string when user is null', () => {
      mockAuth.user.mockReturnValue(null);
      const c = TestBed.createComponent(ShellComponent).componentInstance;
      expect(c.initials()).toBe('');
    });

    it('caps initials at 2 characters for multi-word names', () => {
      mockAuth.user.mockReturnValue({ fullName: 'Alice Mary Smith', roles: [] });
      const c = TestBed.createComponent(ShellComponent).componentInstance;
      expect(c.initials()).toBe('AM');
    });
  });

  // ── navItems computed ─────────────────────────────────────────────────────────

  describe('navItems computed', () => {
    const paths = (c: ShellComponent) => c.navItems().map(n => n.path);

    it('shows only the self-service pages to a person with no management permission', () => {
      const c = createComponent();
      expect(paths(c)).toEqual(['/home', '/my-leave', '/profile']);
    });

    it('adds a management page only when the person holds its permission', () => {
      held = ['staff.view', 'leave.approve'];
      const c = createComponent();
      expect(paths(c)).toEqual(['/home', '/my-leave', '/profile', '/staff', '/leave']);
    });

    it('never shows Dashboard without reports.view', () => {
      held = ['staff.view'];
      const c = createComponent();
      expect(paths(c)).not.toContain('/dashboard');
    });

    it('shows every management page to a person holding every permission', () => {
      held = ['reports.view', 'staff.view', 'leave.approve', 'cashadvance.manage', 'payroll.lock', 'payroll.view', 'roles.manage', 'policy.manage'];
      const c = createComponent();
      expect(paths(c)).toEqual(expect.arrayContaining(['/dashboard', '/staff', '/leave', '/cash-advances', '/payroll-locks', '/payroll-rates', '/roles', '/policy']));
    });

    it('shows Company Policy only to a person holding policy.manage', () => {
      expect(paths(createComponent())).not.toContain('/policy');
      held = ['policy.manage'];
      expect(paths(createComponent())).toContain('/policy');
    });

    it('shows Payroll Rates only to a person holding payroll.view', () => {
      expect(paths(createComponent())).not.toContain('/payroll-rates');
      held = ['payroll.view'];
      expect(paths(createComponent())).toContain('/payroll-rates');
    });

    it('puts "My work" first and drops groups the person cannot use', () => {
      held = ['staff.view'];
      const c = createComponent();
      expect(c.navGroups().map(g => g.label)).toEqual(['My work', 'Operations']);
    });
  });

  // ── leave decision notice ─────────────────────────────────────────────────────

  describe('leave decision notice', () => {
    it('toasts and badges a request that was pending and is now approved', () => {
      localStorage.setItem('nc_leave_status_snapshot', JSON.stringify({ L1: 'pending' }));
      mockLeave.getMine.mockReturnValue(of([{ id: 'L1', status: 'approved' }]));
      const c = createComponent();
      expect(mockToast.success).toHaveBeenCalledWith('Your leave request has been approved.');
      expect(c.leaveChangeCount()).toBe(1);
    });

    it('does not toast a request that was already decided when last seen', () => {
      localStorage.setItem('nc_leave_status_snapshot', JSON.stringify({ L1: 'approved' }));
      mockLeave.getMine.mockReturnValue(of([{ id: 'L1', status: 'approved' }]));
      const c = createComponent();
      expect(mockToast.success).not.toHaveBeenCalled();
      expect(c.leaveChangeCount()).toBe(0);
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
      const profile = c.avatarMenuItems().find(i => i.label === 'Profile');
      expect(profile).toBeDefined();
      expect(profile!.routerLink).toBe('/profile');
    });

    it('includes a Sign Out item with a command callback', () => {
      const c = createComponent();
      const signOut = c.avatarMenuItems().find(i => i.label === 'Sign out');
      expect(signOut).toBeDefined();
      expect(typeof signOut!.command).toBe('function');
    });
  });

  // ── toggleSidebar() ───────────────────────────────────────────────────────────

  describe('toggleSidebar()', () => {
    it('toggles the collapsed state from initial value', () => {
      const c = createComponent();
      const initial = c.collapsed();
      c.toggleSidebar();
      expect(c.collapsed()).toBe(!initial);
    });

    it('toggles back to original when called twice', () => {
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

    it('stops SignalR connection before logging out', async () => {
      const c = createComponent();
      c.logout();
      await new Promise(r => setTimeout(r));   // flush the deferred navigation
      expect(mockSignalR.stopConnection).toHaveBeenCalled();
    });

    it('calls auth.logout() to clear the session', async () => {
      const c = createComponent();
      c.logout();
      await new Promise(r => setTimeout(r));
      expect(mockAuth.logout).toHaveBeenCalled();
    });
  });

  // ── constructor effects ───────────────────────────────────────────────────────

  describe('constructor side effects', () => {
    it('starts the SignalR connection on construction', () => {
      createComponent();
      expect(mockSignalR.startConnection).toHaveBeenCalled();
    });

    it('loads the company policy so leave previews use the working week from the first page', () => {
      createComponent();
      expect(mockPolicyLoad).toHaveBeenCalled();
    });
  });
});
