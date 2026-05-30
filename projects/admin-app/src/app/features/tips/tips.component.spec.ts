import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { TipsComponent } from './tips.component';
import { AdminService, LocationStore, ToastService, TipsPoolDto } from 'shared-lib';

/**
 * TipsComponent — admin tips distribution management.
 *
 * WHY these tests matter:
 *  - distribute() enforces a 50,000 cap — bypassing it risks incorrect payouts
 *    that may be hard to reverse once recorded.
 *  - onLocationChange() must sync locationName from the store — the API uses the
 *    name (not the id), so a stale name causes distribution to the wrong location.
 *  - confirm() and cancel() must clear preview state — a stale preview could be
 *    confirmed a second time by mistake.
 */

function makePool(overrides: Partial<TipsPoolDto> = {}): TipsPoolDto {
  return {
    id: 'pool-1', locationName: 'Main Bar', shiftDate: '2026-05-17',
    totalAmount: 500, splitMethod: 'equal', isDistributed: false,
    staffCount: 5, createdAt: '2026-05-17T20:00:00Z',
    ...overrides,
  };
}

const fakePreview = {
  staffCount: 3,
  allocations: [{ employeeId: 'e1', fullName: 'Jane', amount: 166.67, hoursWorked: 8 }],
  excludedStaff: [],
};

describe('TipsComponent', () => {
  let component: TipsComponent;

  let mockAdminSvc: {
    getTipsPools:   ReturnType<typeof vi.fn>;
    distributeTips: ReturnType<typeof vi.fn>;
  };
  let mockLocationStore: { load: ReturnType<typeof vi.fn>; locations: ReturnType<typeof vi.fn> };
  let mockToastSvc:      { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockAdminSvc = {
      getTipsPools:   vi.fn().mockReturnValue(of([])),
      distributeTips: vi.fn().mockReturnValue(of(fakePreview)),
    };
    mockLocationStore = {
      load:      vi.fn(),
      locations: vi.fn().mockReturnValue([
        { id: 'loc-1', name: 'Main Bar' },
        { id: 'loc-2', name: 'VIP Lounge' },
      ]),
    };
    mockToastSvc = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [TipsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: AdminService,   useValue: mockAdminSvc },
        { provide: LocationStore,  useValue: mockLocationStore },
        { provide: ToastService,   useValue: mockToastSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    component = TestBed.createComponent(TipsComponent).componentInstance;
  });

  afterEach(() => vi.restoreAllMocks());

  // ── ngOnInit() ───────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('loads location list for the dropdown', () => {
      component.ngOnInit();
      expect(mockLocationStore.load).toHaveBeenCalled();
    });

    it('loads existing tips pools for the table', () => {
      component.ngOnInit();
      expect(mockAdminSvc.getTipsPools).toHaveBeenCalled();
    });

    it('populates pools signal with fetched data', () => {
      const pools = [makePool()];
      mockAdminSvc.getTipsPools.mockReturnValue(of(pools));
      component.ngOnInit();
      expect(component.pools()).toEqual(pools);
    });

    it('shows error toast when pools fail to load', () => {
      mockAdminSvc.getTipsPools.mockReturnValue(throwError(() => new Error('Network')));
      component.ngOnInit();
      expect(mockToastSvc.error).toHaveBeenCalledWith('tips.loadFailed');
    });
  });

  // ── openForm() ───────────────────────────────────────────────────────────────

  describe('openForm()', () => {
    it('shows the distribution form', () => {
      component.openForm();
      expect(component.showForm()).toBe(true);
    });

    it('resets the form fields to defaults', () => {
      component.form.locationId = 'loc-1';
      component.form.totalAmount = 999;
      component.openForm();
      expect(component.form.locationId).toBe('');
      expect(component.form.totalAmount).toBe(0);
    });

    it('clears any previous preview', () => {
      component.preview.set(fakePreview);
      component.openForm();
      expect(component.preview()).toBeNull();
    });
  });

  // ── onLocationChange() ───────────────────────────────────────────────────────

  describe('onLocationChange()', () => {
    it('syncs locationName when a matching location is found', () => {
      component.form.locationId = 'loc-2';
      component.onLocationChange();
      expect(component.form.locationName).toBe('VIP Lounge');
    });

    it('leaves locationName unchanged when locationId does not match any location', () => {
      component.form.locationId   = 'loc-nonexistent';
      component.form.locationName = 'Old Name';
      component.onLocationChange();
      expect(component.form.locationName).toBe('Old Name');
    });
  });

  // ── distribute() — validation ─────────────────────────────────────────────────

  describe('distribute() — validation', () => {
    it('shows error toast and does not call API when locationName is missing', () => {
      component.form.locationName = '';
      component.form.shiftDate    = '2026-05-17';
      component.form.totalAmount  = 500;
      component.distribute();
      expect(mockToastSvc.error).toHaveBeenCalledWith('tips.fillRequired');
      expect(mockAdminSvc.distributeTips).not.toHaveBeenCalled();
    });

    it('shows error toast when totalAmount is 0 or negative', () => {
      component.form.locationName = 'Main Bar';
      component.form.shiftDate    = '2026-05-17';
      component.form.totalAmount  = 0;
      component.distribute();
      expect(mockToastSvc.error).toHaveBeenCalledWith('tips.fillRequired');
    });

    it('blocks amounts above 50,000 to prevent runaway payouts', () => {
      component.form.locationName = 'Main Bar';
      component.form.shiftDate    = '2026-05-17';
      component.form.totalAmount  = 50_001;
      component.distribute();
      expect(mockToastSvc.error).toHaveBeenCalledWith('tips.capExceeded');
      expect(mockAdminSvc.distributeTips).not.toHaveBeenCalled();
    });

    it('allows exactly 50,000 — it is the cap, not below it', () => {
      component.form.locationName = 'Main Bar';
      component.form.shiftDate    = '2026-05-17';
      component.form.totalAmount  = 50_000;
      component.distribute();
      expect(mockAdminSvc.distributeTips).toHaveBeenCalled();
    });
  });

  // ── distribute() — success and error ─────────────────────────────────────────

  describe('distribute() — success', () => {
    beforeEach(() => {
      component.form.locationName = 'Main Bar';
      component.form.shiftDate    = '2026-05-17';
      component.form.totalAmount  = 500;
    });

    it('calls distributeTips with the form data', () => {
      component.distribute();
      expect(mockAdminSvc.distributeTips).toHaveBeenCalledWith(
        expect.objectContaining({ locationName: 'Main Bar', totalAmount: 500 }),
      );
    });

    it('populates preview signal with the distribution result', () => {
      component.distribute();
      expect(component.preview()).toEqual(fakePreview);
    });

    it('sets previewTime to a non-null string', () => {
      component.distribute();
      expect(component.previewTime()).not.toBeNull();
    });

    it('clears distributing flag after success', () => {
      component.distribute();
      expect(component.distributing()).toBe(false);
    });

    it('shows error toast and clears distributing flag on API failure', () => {
      mockAdminSvc.distributeTips.mockReturnValue(throwError(() => new Error('Network')));
      component.distribute();
      expect(mockToastSvc.error).toHaveBeenCalledWith('tips.distributionFailed');
      expect(component.distributing()).toBe(false);
    });
  });

  // ── confirm() ────────────────────────────────────────────────────────────────

  describe('confirm()', () => {
    it('hides the form', () => {
      component.showForm.set(true);
      component.confirm();
      expect(component.showForm()).toBe(false);
    });

    it('clears preview so a stale result cannot be confirmed again', () => {
      component.preview.set(fakePreview);
      component.confirm();
      expect(component.preview()).toBeNull();
    });

    it('clears previewTime', () => {
      component.previewTime.set('2026-05-17 20:00:00');
      component.confirm();
      expect(component.previewTime()).toBeNull();
    });

    it('reloads pools after confirming distribution', () => {
      mockAdminSvc.getTipsPools.mockClear();
      component.confirm();
      expect(mockAdminSvc.getTipsPools).toHaveBeenCalled();
    });
  });

  // ── cancel() ─────────────────────────────────────────────────────────────────

  describe('cancel()', () => {
    it('hides the form', () => {
      component.showForm.set(true);
      component.cancel();
      expect(component.showForm()).toBe(false);
    });

    it('clears preview and previewTime', () => {
      component.preview.set(fakePreview);
      component.previewTime.set('now');
      component.cancel();
      expect(component.preview()).toBeNull();
      expect(component.previewTime()).toBeNull();
    });

    it('does NOT reload pools — cancel discards the preview without persisting', () => {
      mockAdminSvc.getTipsPools.mockClear();
      component.cancel();
      expect(mockAdminSvc.getTipsPools).not.toHaveBeenCalled();
    });
  });
});
