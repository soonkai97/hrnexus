import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { FeatureFlagsComponent } from './feature-flags.component';
import { FeatureFlagService, ToastService } from 'shared-lib';

/**
 * FeatureFlagsComponent — admin feature toggle panel.
 *
 * WHY these tests matter:
 *  - toggle() is the primary admin action; an error without user feedback leaves
 *    admins unsure whether the toggle actually changed anything.
 *  - enableAll() / disableAll() are bulk ops — a toast confirms success and errors
 *    must surface immediately since all features in the venue depend on this.
 *  - featuresForPhase() drives the grouped UI layout; wrong phase mapping would
 *    show features in the wrong phase group.
 *  - phaseLabel() returns an i18n key — a typo would silently show an empty label.
 *  - isEnabled() delegates to the service — tests confirm the wiring is correct.
 */

describe('FeatureFlagsComponent', () => {
  let component: FeatureFlagsComponent;

  let mockFeature: {
    flags:      ReturnType<typeof vi.fn>;
    isEnabled:  ReturnType<typeof vi.fn>;
    toggle:     ReturnType<typeof vi.fn>;
    enableAll:  ReturnType<typeof vi.fn>;
    disableAll: ReturnType<typeof vi.fn>;
    load:       ReturnType<typeof vi.fn>;
  };
  let mockToast:     { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockFeature = {
      flags:      vi.fn().mockReturnValue({ payroll: false, tips: false, tipsHistory: false, paySummary: false, announcements: false, staffLedger: false }),
      isEnabled:  vi.fn().mockReturnValue(false),
      toggle:     vi.fn().mockReturnValue(of(undefined)),
      enableAll:  vi.fn().mockReturnValue(of(undefined)),
      disableAll: vi.fn().mockReturnValue(of(undefined)),
      load:       vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [FeatureFlagsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: FeatureFlagService, useValue: mockFeature },
        { provide: ToastService,       useValue: mockToast },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(FeatureFlagsComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── featuresForPhase() ────────────────────────────────────────────────────────

  describe('featuresForPhase()', () => {
    it('returns phase-1 features (payroll, tips, paySummary, tipsHistory)', () => {
      const p1 = component.featuresForPhase(1);
      const keys = p1.map(f => f.key);
      expect(keys).toContain('payroll');
      expect(keys).toContain('tips');
      expect(keys).toContain('paySummary');
      expect(keys).toContain('tipsHistory');
    });

    it('returns phase-2 features (announcements)', () => {
      const p2 = component.featuresForPhase(2);
      expect(p2.map(f => f.key)).toContain('announcements');
    });

    it('returns phase-3 features (staffLedger)', () => {
      const p3 = component.featuresForPhase(3);
      expect(p3.map(f => f.key)).toContain('staffLedger');
    });

    it('returns empty array for a phase with no features', () => {
      expect(component.featuresForPhase(99)).toEqual([]);
    });

    it('does not include phase-2 features in phase-1 results', () => {
      const p1 = component.featuresForPhase(1);
      expect(p1.map(f => f.key)).not.toContain('announcements');
    });
  });

  // ── phaseLabel() ──────────────────────────────────────────────────────────────

  describe('phaseLabel()', () => {
    it('returns the correct i18n key for phase 1', () => {
      expect(component.phaseLabel(1)).toBe('features.phase1Label');
    });

    it('returns the correct i18n key for phase 2', () => {
      expect(component.phaseLabel(2)).toBe('features.phase2Label');
    });

    it('returns the correct i18n key for phase 3', () => {
      expect(component.phaseLabel(3)).toBe('features.phase3Label');
    });
  });

  // ── isEnabled() ───────────────────────────────────────────────────────────────

  describe('isEnabled()', () => {
    it('delegates to featureSvc.isEnabled()', () => {
      mockFeature.isEnabled.mockReturnValue(true);
      expect(component.isEnabled('payroll')).toBe(true);
      expect(mockFeature.isEnabled).toHaveBeenCalledWith('payroll');
    });

    it('returns false when the feature is disabled', () => {
      mockFeature.isEnabled.mockReturnValue(false);
      expect(component.isEnabled('tips')).toBe(false);
    });
  });

  // ── toggle() ─────────────────────────────────────────────────────────────────

  describe('toggle()', () => {
    it('calls featureSvc.toggle() with the given key', () => {
      component.toggle('payroll');
      expect(mockFeature.toggle).toHaveBeenCalledWith('payroll');
    });

    it('shows error toast when toggle fails', () => {
      mockFeature.toggle.mockReturnValue(throwError(() => new Error('Network')));
      component.toggle('payroll');
      expect(mockToast.error).toHaveBeenCalled();
    });

    it('does NOT show error toast when toggle succeeds', () => {
      component.toggle('payroll');
      expect(mockToast.error).not.toHaveBeenCalled();
    });
  });

  // ── enableAll() ───────────────────────────────────────────────────────────────

  describe('enableAll()', () => {
    it('calls featureSvc.enableAll()', () => {
      component.enableAll();
      expect(mockFeature.enableAll).toHaveBeenCalled();
    });

    it('shows success toast after enabling all features', () => {
      component.enableAll();
      expect(mockToast.success).toHaveBeenCalled();
    });

    it('shows error toast when enableAll fails', () => {
      mockFeature.enableAll.mockReturnValue(throwError(() => new Error('Network')));
      component.enableAll();
      expect(mockToast.error).toHaveBeenCalled();
    });
  });

  // ── disableAll() ─────────────────────────────────────────────────────────────

  describe('disableAll()', () => {
    it('calls featureSvc.disableAll()', () => {
      component.disableAll();
      expect(mockFeature.disableAll).toHaveBeenCalled();
    });

    it('shows success toast after disabling all features', () => {
      component.disableAll();
      expect(mockToast.success).toHaveBeenCalled();
    });

    it('shows error toast when disableAll fails', () => {
      mockFeature.disableAll.mockReturnValue(throwError(() => new Error('Network')));
      component.disableAll();
      expect(mockToast.error).toHaveBeenCalled();
    });
  });

  // ── features list ─────────────────────────────────────────────────────────────

  describe('features list', () => {
    it('contains exactly 6 feature definitions', () => {
      expect(component.features.length).toBe(6);
    });

    it('has phases [1, 2, 3]', () => {
      expect(component.phases).toEqual([1, 2, 3]);
    });

    it('every feature has a non-empty key, label, and description', () => {
      component.features.forEach(f => {
        expect(f.key).toBeTruthy();
        expect(f.label).toBeTruthy();
        expect(f.description).toBeTruthy();
      });
    });
  });
});
