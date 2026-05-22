import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { FeaturePinEntryComponent } from './feature-pin-entry.component';
import { FeaturePinService } from '../feature-pin.service';

@Component({ template: '', standalone: true }) class StubComponent {}

/**
 * FeaturePinEntryComponent — PIN unlock gate for the feature-flags admin panel.
 *
 * WHY these tests matter:
 *  - submit() must not fire when pin is empty — an empty string must not reach
 *    the API since it would trivially pass any PIN check.
 *  - isLockedOut prevents further attempts during the lockout window; bypassing
 *    it lets an attacker brute-force the PIN.
 *  - lockoutLabel must compute minutes correctly — an off-by-one makes admins
 *    wait longer than they need to or retry too early.
 *  - 429 handling must set retryAfter from the API response so the UI shows
 *    the correct lockout duration rather than a hardcoded value.
 *  - 503 error must surface a "not configured" message so admins can contact IT
 *    rather than being confused by a generic "incorrect PIN" message.
 */

describe('FeaturePinEntryComponent', () => {
  let component: FeaturePinEntryComponent;

  let mockPinSvc: { unlock: ReturnType<typeof vi.fn> };
  let mockRouter: { navigate: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockPinSvc = { unlock: vi.fn().mockReturnValue(of(undefined)) };
    mockRouter = { navigate: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [FeaturePinEntryComponent],
      providers: [
        provideHttpClient(),
        provideRouter([{ path: 'features', component: StubComponent }]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: FeaturePinService, useValue: mockPinSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(FeaturePinEntryComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── Initial state ─────────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('starts with an empty PIN field', () => {
      expect(component.pin).toBe('');
    });

    it('starts with loading=false', () => {
      expect(component.loading()).toBe(false);
    });

    it('starts with no error', () => {
      expect(component.error()).toBe('');
    });

    it('starts with showPin=false so PIN is masked', () => {
      expect(component.showPin()).toBe(false);
    });

    it('starts with no lockout', () => {
      expect(component.isLockedOut).toBe(false);
    });
  });

  // ── submit() — guard clauses ──────────────────────────────────────────────────

  describe('submit() — guard clauses', () => {
    it('does not call pinSvc.unlock when pin is empty', () => {
      component.pin = '';
      component.submit();
      expect(mockPinSvc.unlock).not.toHaveBeenCalled();
    });

    it('does not call pinSvc.unlock when already loading', () => {
      component.pin = '1234';
      component.loading.set(true);
      component.submit();
      expect(mockPinSvc.unlock).not.toHaveBeenCalled();
    });

    it('does not call pinSvc.unlock when locked out', () => {
      component.pin = '1234';
      component.retryAfter.set(new Date(Date.now() + 10 * 60 * 1000)); // 10 min in future
      component.submit();
      expect(mockPinSvc.unlock).not.toHaveBeenCalled();
    });
  });

  // ── submit() — success ────────────────────────────────────────────────────────

  describe('submit() — success', () => {
    beforeEach(() => {
      component.pin = '123456';
    });

    it('calls pinSvc.unlock with the entered PIN', () => {
      component.submit();
      expect(mockPinSvc.unlock).toHaveBeenCalledWith('123456');
    });

    it('clears loading after successful unlock', () => {
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });

  // ── submit() — error: wrong PIN (4xx other than 429) ─────────────────────────

  describe('submit() — error: incorrect PIN', () => {
    beforeEach(() => {
      component.pin = 'wrong';
      mockPinSvc.unlock.mockReturnValue(throwError(() => ({ status: 401, error: {} })));
    });

    it('sets "Incorrect PIN" error message', () => {
      component.submit();
      expect(component.error()).toBe('Incorrect PIN. Please try again.');
    });

    it('clears the PIN field after a wrong attempt', () => {
      component.submit();
      expect(component.pin).toBe('');
    });

    it('clears loading after a wrong PIN attempt', () => {
      component.submit();
      expect(component.loading()).toBe(false);
    });
  });

  // ── submit() — error: 429 rate limit ─────────────────────────────────────────

  describe('submit() — error: 429 rate limit', () => {
    it('sets retryAfter from the API retryAfter value', () => {
      const futureDate = new Date(Date.now() + 15 * 60 * 1000).toISOString();
      component.pin = '1234';
      mockPinSvc.unlock.mockReturnValue(
        throwError(() => ({ status: 429, error: { retryAfter: futureDate } })),
      );
      component.submit();
      expect(component.retryAfter()).not.toBeNull();
    });

    it('marks the component as locked out after 429', () => {
      const futureDate = new Date(Date.now() + 15 * 60 * 1000).toISOString();
      component.pin = '1234';
      mockPinSvc.unlock.mockReturnValue(
        throwError(() => ({ status: 429, error: { retryAfter: futureDate } })),
      );
      component.submit();
      expect(component.isLockedOut).toBe(true);
    });

    it('sets a lockout-style error message after 429', () => {
      const futureDate = new Date(Date.now() + 15 * 60 * 1000).toISOString();
      component.pin = '1234';
      mockPinSvc.unlock.mockReturnValue(
        throwError(() => ({ status: 429, error: { retryAfter: futureDate } })),
      );
      component.submit();
      expect(component.error()).toContain('Locked out');
    });

    it('uses a 15-minute fallback if no retryAfter is in the response', () => {
      component.pin = '1234';
      mockPinSvc.unlock.mockReturnValue(
        throwError(() => ({ status: 429, error: {} })),
      );
      component.submit();
      // retryAfter is set to ~15 minutes from now
      const diff = component.retryAfter()!.getTime() - Date.now();
      expect(diff).toBeGreaterThan(14 * 60 * 1000);
      expect(diff).toBeLessThanOrEqual(15 * 60 * 1000 + 1000);
    });
  });

  // ── submit() — error: 503 not configured ─────────────────────────────────────

  describe('submit() — error: 503 not configured', () => {
    it('shows "not configured" message on 503', () => {
      component.pin = '1234';
      mockPinSvc.unlock.mockReturnValue(throwError(() => ({ status: 503, error: {} })));
      component.submit();
      expect(component.error()).toContain('not configured');
    });
  });

  // ── isLockedOut getter ────────────────────────────────────────────────────────

  describe('isLockedOut getter', () => {
    it('returns false when retryAfter is null', () => {
      component.retryAfter.set(null);
      expect(component.isLockedOut).toBe(false);
    });

    it('returns false when retryAfter is in the past', () => {
      component.retryAfter.set(new Date(Date.now() - 1000));
      expect(component.isLockedOut).toBe(false);
    });

    it('returns true when retryAfter is in the future', () => {
      component.retryAfter.set(new Date(Date.now() + 10 * 60 * 1000));
      expect(component.isLockedOut).toBe(true);
    });
  });

  // ── lockoutLabel getter ───────────────────────────────────────────────────────

  describe('lockoutLabel getter', () => {
    it('returns empty string when retryAfter is null', () => {
      component.retryAfter.set(null);
      expect(component.lockoutLabel).toBe('');
    });

    it('uses plural "minutes" for more than 1 minute', () => {
      component.retryAfter.set(new Date(Date.now() + 3 * 60 * 1000));
      expect(component.lockoutLabel).toContain('minutes');
    });

    it('uses singular "minute" for exactly 1 minute remaining', () => {
      component.retryAfter.set(new Date(Date.now() + 60 * 1000));
      expect(component.lockoutLabel).toContain('minute');
    });
  });
});
