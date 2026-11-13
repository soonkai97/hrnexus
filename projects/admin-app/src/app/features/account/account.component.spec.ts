import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { AccountComponent } from './account.component';
import { AuthService, StaffManagementService, LangService, VersionService } from 'shared-lib';

/**
 * AccountComponent — admin account profile page.
 *
 * WHY these tests matter:
 *  - initials computed must handle nulls, empty strings, and multi-word names —
 *    incorrect initials break the avatar badge that every admin sees in the top bar.
 *  - ngOnInit() must fetch the full profile — without it the account page shows
 *    stale JWT data (no phone number, role detail, etc.).
 *  - logout() must delegate to AuthService — an incorrect call leaves the admin
 *    session open even after clicking "Sign out".
 */

const fakeProfile = {
  id: 'e1', staffId: 'NC001', firstName: 'Jane', lastName: 'Doe',
  email: 'jane@example.com', roles: ['manager'],
};

describe('AccountComponent', () => {
  let mockAuth:     { user: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };
  let mockStaffSvc: { getMyProfile: ReturnType<typeof vi.fn> };
  let mockLangSvc:  { current: ReturnType<typeof vi.fn>; set: ReturnType<typeof vi.fn> };

  function createComponent(fullName: string | null = 'Jane Doe') {
    mockAuth.user.mockReturnValue(
      fullName !== null
        ? { id: 'e1', staffId: 'NC001', fullName, email: 'jane@example.com', roles: [] }
        : null,
    );
    return TestBed.createComponent(AccountComponent).componentInstance;
  }

  beforeEach(async () => {
    mockAuth     = { user: vi.fn().mockReturnValue(null), logout: vi.fn() };
    mockStaffSvc = { getMyProfile: vi.fn().mockReturnValue(of(fakeProfile)) };
    mockLangSvc  = { current: vi.fn().mockReturnValue('en'), set: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [AccountComponent],
      providers: [
        provideHttpClient(withXhr()),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AuthService,  useValue: mockAuth },
        { provide: StaffManagementService, useValue: mockStaffSvc },
        { provide: LangService,  useValue: mockLangSvc },
        { provide: VersionService, useValue: { info: () => ({ version: 'dev' }), load: vi.fn() } },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  // ── initials computed ─────────────────────────────────────────────────────────

  describe('initials computed', () => {
    it('returns two uppercase initials for a two-word name', () => {
      const c = createComponent('Jane Doe');
      expect(c.initials()).toBe('JD');
    });

    it('returns one initial for a single-word name', () => {
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
      const c = createComponent(null);
      expect(c.initials()).toBe('');
    });

    it('returns empty string when fullName is blank', () => {
      const c = createComponent('');
      expect(c.initials()).toBe('');
    });
  });

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('fetches the staff profile so the page shows current details', () => {
      const component = createComponent();
      component.ngOnInit();
      expect(mockStaffSvc.getMyProfile).toHaveBeenCalled();
    });

    it('populates the profile signal with the API response', () => {
      const component = createComponent();
      component.ngOnInit();
      expect(component.profile()).toEqual(fakeProfile);
    });
  });

  // ── logout() ─────────────────────────────────────────────────────────────────

  describe('logout()', () => {
    it('delegates to AuthService.logout()', () => {
      const component = createComponent();
      component.logout();
      expect(mockAuth.logout).toHaveBeenCalled();
    });
  });
});
