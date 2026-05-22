import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { ChangePasswordPageComponent } from './change-password-page.component';
import { AuthService, LangService } from 'shared-lib';

/**
 * ChangePasswordPageComponent (admin-app) — account settings password change wrapper.
 *
 * WHY these tests matter:
 *  - This is a thin wrapper around ChangePasswordComponent from shared-lib.
 *    The test confirms it can be instantiated without errors (no missing providers)
 *    and has no component-level logic to verify — the behaviour lives in
 *    ChangePasswordComponent which is tested in shared-lib.
 *  - If a new injection is accidentally added to this component without a provider,
 *    this smoke test catches it immediately.
 */

describe('ChangePasswordPageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChangePasswordPageComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AuthService, useValue: { user: vi.fn().mockReturnValue(null), changePassword: vi.fn() } },
        { provide: LangService, useValue: { current: vi.fn().mockReturnValue('en'), set: vi.fn() } },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
  });

  afterEach(() => vi.restoreAllMocks());

  it('can be instantiated without provider errors', () => {
    const component = TestBed.createComponent(ChangePasswordPageComponent).componentInstance;
    expect(component).toBeTruthy();
  });
});
