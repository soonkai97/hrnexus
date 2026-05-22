import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ProfileComponent } from './profile.component';
import { AuthService, StaffManagementService, ToastService, PushService, LangService } from 'shared-lib';

/**
 * ProfileComponent — staff profile view with push notification toggle.
 *
 * WHY these tests matter:
 *  - initials computed must handle nulls and single-word names — blank or crashed
 *    initials make the avatar widget look broken for the staff member.
 *  - togglePush() unsubscribe path must call the API so the server removes the
 *    endpoint; without it the server keeps sending dead pushes and wastes quota.
 *  - togglePush() subscribe path validates publicKey presence and permission
 *    grant — silent failures would leave pushEnabled in an inconsistent state.
 *  - pushLoading must always clear in the finally block — a stuck loading spinner
 *    prevents the staff member from retrying.
 */

// Minimal StaffDetail shape for profile
const fakeProfile = {
  id: 'e1', staffId: 'NC001', firstName: 'Jane', lastName: 'Doe',
  email: 'jane@example.com', roles: ['bartender'],
};

// A real-looking PushSubscription stub
function makePushSub(endpoint = 'https://push.example.com/sub') {
  return {
    endpoint,
    unsubscribe: vi.fn().mockResolvedValue(true),
    toJSON: vi.fn().mockReturnValue({
      endpoint,
      keys: { p256dh: 'mock-p256dh', auth: 'mock-auth' },
    }),
  };
}

describe('ProfileComponent', () => {
  let component: ProfileComponent;

  let mockAuth:    { user: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };
  let mockStaffSvc: { getMyProfile: ReturnType<typeof vi.fn> };
  let mockToastSvc: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockPushSvc: {
    getPublicKey:       ReturnType<typeof vi.fn>;
    requestPermission: ReturnType<typeof vi.fn>;
    subscribe:          ReturnType<typeof vi.fn>;
    unsubscribe:        ReturnType<typeof vi.fn>;
  };
  let mockLangSvc: { current: ReturnType<typeof vi.fn>; set: ReturnType<typeof vi.fn>; toggle: ReturnType<typeof vi.fn> };

  function createComponent(fullName = 'Jane Doe') {
    mockAuth.user.mockReturnValue({ id: 'e1', staffId: 'NC001', fullName, email: 'j@ex.com', roles: [] });
    return TestBed.createComponent(ProfileComponent).componentInstance;
  }

  beforeEach(async () => {
    mockAuth     = { user: vi.fn().mockReturnValue(null), logout: vi.fn() };
    mockStaffSvc = { getMyProfile: vi.fn().mockReturnValue(of(fakeProfile)) };
    mockToastSvc = { success: vi.fn(), error: vi.fn() };
    mockPushSvc  = {
      getPublicKey:      vi.fn().mockReturnValue(of({ publicKey: 'mock-key' })),
      requestPermission: vi.fn().mockResolvedValue(makePushSub()),
      subscribe:         vi.fn().mockReturnValue(of(undefined)),
      unsubscribe:       vi.fn().mockReturnValue(of(undefined)),
    };
    mockLangSvc  = { current: vi.fn().mockReturnValue('en'), set: vi.fn(), toggle: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [ProfileComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AuthService,   useValue: mockAuth },
        { provide: StaffManagementService,  useValue: mockStaffSvc },
        { provide: ToastService,  useValue: mockToastSvc },
        { provide: PushService,   useValue: mockPushSvc },
        { provide: LangService,   useValue: mockLangSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = createComponent();
  });

  afterEach(() => vi.restoreAllMocks());

  // ── initials computed ─────────────────────────────────────────────────────────

  describe('initials computed', () => {
    it('returns first two initials of a full name — "Jane Doe" → "JD"', () => {
      const c = createComponent('Jane Doe');
      expect(c.initials()).toBe('JD');
    });

    it('returns single initial for a single-word name — "Jane" → "J"', () => {
      const c = createComponent('Jane');
      expect(c.initials()).toBe('J');
    });

    it('uppercases initials regardless of name casing', () => {
      const c = createComponent('alice smith');
      expect(c.initials()).toBe('AS');
    });

    it('returns at most 2 characters for names with more than two words', () => {
      const c = createComponent('Mary Jane Watson');
      expect(c.initials()).toBe('MJ');
    });

    it('returns empty string when user is null', () => {
      mockAuth.user.mockReturnValue(null);
      const c = TestBed.createComponent(ProfileComponent).componentInstance;
      expect(c.initials()).toBe('');
    });

    it('returns empty string when fullName is an empty string', () => {
      const c = createComponent('');
      expect(c.initials()).toBe('');
    });
  });

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('fetches the staff profile on init', () => {
      component.ngOnInit();
      expect(mockStaffSvc.getMyProfile).toHaveBeenCalled();
    });

    it('populates the profile signal with the fetched data', () => {
      component.ngOnInit();
      expect(component.profile()).toEqual(fakeProfile);
    });

    it('does not crash when getMyProfile fails — error is swallowed silently', () => {
      mockStaffSvc.getMyProfile.mockReturnValue({ subscribe: (obs: any) => obs.error(new Error('Network')) });
      expect(() => component.ngOnInit()).not.toThrow();
    });

    it('does not call pushEnabled when serviceWorker is absent — jsdom environment', () => {
      // In jsdom, 'serviceWorker' is not in navigator; the branch is skipped silently
      const spy = vi.spyOn(component.pushEnabled, 'set');
      component.ngOnInit();
      expect(spy).not.toHaveBeenCalled();
    });
  });

  // ── logout() ─────────────────────────────────────────────────────────────────

  describe('logout()', () => {
    it('delegates to AuthService.logout()', () => {
      component.logout();
      expect(mockAuth.logout).toHaveBeenCalled();
    });
  });

  // ── togglePush() — unsubscribe path ──────────────────────────────────────────

  describe('togglePush() — unsubscribe (existing subscription)', () => {
    let mockSub: ReturnType<typeof makePushSub>;
    let mockReg: { pushManager: { getSubscription: ReturnType<typeof vi.fn> } };

    beforeEach(() => {
      mockSub = makePushSub();
      mockReg = { pushManager: { getSubscription: vi.fn().mockResolvedValue(mockSub) } };
      Object.defineProperty(navigator, 'serviceWorker', {
        value: { ready: Promise.resolve(mockReg) },
        configurable: true,
        writable: true,
      });
    });

    afterEach(() => {
      try { delete (navigator as any).serviceWorker; } catch { /* non-configurable — leave it */ }
    });

    it('calls unsubscribe() on the existing push subscription', async () => {
      await component.togglePush();
      expect(mockSub.unsubscribe).toHaveBeenCalled();
    });

    it('calls pushSvc.unsubscribe with the subscription endpoint', async () => {
      await component.togglePush();
      expect(mockPushSvc.unsubscribe).toHaveBeenCalledWith('https://push.example.com/sub');
    });

    it('sets pushEnabled to false after unsubscribing', async () => {
      component.pushEnabled.set(true);
      await component.togglePush();
      expect(component.pushEnabled()).toBe(false);
    });

    it('clears pushLoading after unsubscribing — spinner must not stay stuck', async () => {
      await component.togglePush();
      expect(component.pushLoading()).toBe(false);
    });
  });

  // ── togglePush() — subscribe path ────────────────────────────────────────────

  describe('togglePush() — subscribe (no existing subscription)', () => {
    let mockSub: ReturnType<typeof makePushSub>;
    let mockReg: { pushManager: { getSubscription: ReturnType<typeof vi.fn> } };

    beforeEach(() => {
      mockSub = makePushSub();
      mockReg = { pushManager: { getSubscription: vi.fn().mockResolvedValue(null) } };
      Object.defineProperty(navigator, 'serviceWorker', {
        value: { ready: Promise.resolve(mockReg) },
        configurable: true,
        writable: true,
      });
      mockPushSvc.requestPermission.mockResolvedValue(mockSub);
    });

    afterEach(() => {
      try { delete (navigator as any).serviceWorker; } catch { /* leave it */ }
    });

    it('fetches the VAPID public key before subscribing', async () => {
      await component.togglePush();
      expect(mockPushSvc.getPublicKey).toHaveBeenCalled();
    });

    it('calls requestPermission with the VAPID public key', async () => {
      await component.togglePush();
      expect(mockPushSvc.requestPermission).toHaveBeenCalledWith('mock-key');
    });

    it('calls pushSvc.subscribe with the subscription data', async () => {
      await component.togglePush();
      expect(mockPushSvc.subscribe).toHaveBeenCalledWith(
        expect.objectContaining({ endpoint: 'https://push.example.com/sub' }),
      );
    });

    it('sets pushEnabled to true after subscribing', async () => {
      component.pushEnabled.set(false);
      await component.togglePush();
      expect(component.pushEnabled()).toBe(true);
    });

    it('clears pushLoading after subscribing', async () => {
      await component.togglePush();
      expect(component.pushLoading()).toBe(false);
    });

    it('shows error toast and does not subscribe when publicKey is missing', async () => {
      mockPushSvc.getPublicKey.mockReturnValue(of({ publicKey: '' }));
      await component.togglePush();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Push not configured.');
      expect(mockPushSvc.subscribe).not.toHaveBeenCalled();
    });

    it('shows error toast and does not subscribe when permission is denied (requestPermission returns null)', async () => {
      mockPushSvc.requestPermission.mockResolvedValue(null);
      await component.togglePush();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Permission denied.');
      expect(mockPushSvc.subscribe).not.toHaveBeenCalled();
    });
  });

  // ── togglePush() — error handling ────────────────────────────────────────────

  describe('togglePush() — error handling', () => {
    beforeEach(() => {
      Object.defineProperty(navigator, 'serviceWorker', {
        value: { ready: Promise.reject(new Error('SW error')) },
        configurable: true,
        writable: true,
      });
    });

    afterEach(() => {
      try { delete (navigator as any).serviceWorker; } catch { /* leave it */ }
    });

    it('shows push error toast when an exception is thrown', async () => {
      await component.togglePush();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Push notification error.');
    });

    it('clears pushLoading even when an exception is thrown — finally block', async () => {
      await component.togglePush();
      expect(component.pushLoading()).toBe(false);
    });
  });
});
