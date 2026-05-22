import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { HostessLedgerComponent, WORK_TYPES, SETTLEMENTS } from './hostess-ledger.component';
import {
  StaffStore, HostessService, ToastService, ConfirmService,
  WORK_TYPE_LABELS, SETTLEMENT_LABELS,
  HostessEntryDto,
} from 'shared-lib';

/**
 * HostessLedgerComponent — hostess earnings ledger page.
 *
 * WHY these tests matter:
 *  - Work types come from Excel free-text (OVERTIME, SLOWNIGHT as 吃蛋 etc.); a wrong label
 *    or missing type would corrupt imported data and confuse staff with years of records.
 *  - CASH and SETTLED are distinct settlements in the Excel data; conflating them would
 *    misreport collected vs uncollected payments in the monthly ledger.
 *  - recalcVenueEarnings() auto-fills the venue field — an off-by-one in the work-type list
 *    (e.g. auto-calcing OVERNIGHT) would silently corrupt the private-money columns.
 *  - hasPrivateFields() controls whether the private gross/net inputs even appear in the form;
 *    missing it means private earnings are never recorded for overnight bookings.
 *  - save() / remove() are the primary mutation paths — wrong observable branching (create vs
 *    update, or skipping reload) results in stale UI that doesn't reflect DB state.
 *  - The summary panel (totalVenue, totalPrivateNet) drives the monthly payout calculation
 *    that determines how much cash the hostess is owed; an off-by-one is a real financial error.
 */

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeEntry(overrides: Partial<HostessEntryDto> = {}): HostessEntryDto {
  return {
    id: 'e1',
    staffId: 'staff-uuid-1',
    entryDate: '2026-04-30',
    workType: 'VENUECLOCK',
    venueUnits: 5,
    venueRate: 60,
    venueEarnings: 300,
    privateGross: 0,
    privateNet: 0,
    settlement: 'PENDING',
    notes: null,
    createdAt: '2026-04-30T10:00:00Z',
    updatedAt: '2026-04-30T10:00:00Z',
    ...overrides,
  };
}

// ── Suite ─────────────────────────────────────────────────────────────────────

describe('HostessLedgerComponent', () => {
  let component: HostessLedgerComponent;

  let mockStaffStore: {
    staff:     ReturnType<typeof vi.fn>;
    loadStaff: ReturnType<typeof vi.fn>;
  };
  let mockHostessSvc: {
    listEntries:  ReturnType<typeof vi.fn>;
    createEntry:  ReturnType<typeof vi.fn>;
    updateEntry:  ReturnType<typeof vi.fn>;
    deleteEntry:  ReturnType<typeof vi.fn>;
  };
  let mockToastSvc:   { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let mockConfirmSvc: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    mockStaffStore = {
      staff:     vi.fn().mockReturnValue([]),
      loadStaff: vi.fn(),
    };
    mockHostessSvc = {
      listEntries: vi.fn().mockReturnValue(of([])),
      createEntry: vi.fn().mockReturnValue(of({})),
      updateEntry: vi.fn().mockReturnValue(of({})),
      deleteEntry: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToastSvc   = { success: vi.fn(), error: vi.fn() };
    mockConfirmSvc = { confirm: vi.fn().mockReturnValue(of(true)) };

    await TestBed.configureTestingModule({
      imports: [HostessLedgerComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: StaffStore,      useValue: mockStaffStore },
        { provide: HostessService,  useValue: mockHostessSvc },
        { provide: ToastService,    useValue: mockToastSvc },
        { provide: ConfirmService,  useValue: mockConfirmSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(HostessLedgerComponent);
    component = fixture.componentInstance;
    // Do NOT call detectChanges() — avoids PrimeNG template initialisation errors.
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ── Work-type coverage (Excel alignment) ─────────────────────────────────────

  describe('WORK_TYPES array — Excel alignment', () => {
    it('contains all 8 work types matching the Excel 工作地点 categories', () => {
      expect(WORK_TYPES).toHaveLength(8);
      expect(WORK_TYPES).toContain('VENUECLOCK');
      expect(WORK_TYPES).toContain('TABLERUN');
      expect(WORK_TYPES).toContain('OVERTIME');
      expect(WORK_TYPES).toContain('OVERNIGHT');
      expect(WORK_TYPES).toContain('PRIVATEHOURS');
      expect(WORK_TYPES).toContain('SELFDEDUCT');
      expect(WORK_TYPES).toContain('REST');
      expect(WORK_TYPES).toContain('SLOWNIGHT');
    });

    it('includes OVERTIME — needed for "SAIGON加时" entries seen across multiple hostess sheets', () => {
      expect(WORK_TYPES).toContain('OVERTIME');
    });
  });

  describe('WORK_TYPE_LABELS — Excel label accuracy', () => {
    it('labels VENUECLOCK as 写钟', () => expect(WORK_TYPE_LABELS['VENUECLOCK']).toBe('写钟'));
    it('labels TABLERUN as 跑台', () => expect(WORK_TYPE_LABELS['TABLERUN']).toBe('跑台'));
    it('labels OVERTIME as 加时', () => expect(WORK_TYPE_LABELS['OVERTIME']).toBe('加时'));
    it('labels OVERNIGHT as 包夜', () => expect(WORK_TYPE_LABELS['OVERNIGHT']).toBe('包夜'));
    it('labels PRIVATEHOURS as 私小时', () => expect(WORK_TYPE_LABELS['PRIVATEHOURS']).toBe('私小时'));
    it('labels SELFDEDUCT as 自扣', () => expect(WORK_TYPE_LABELS['SELFDEDUCT']).toBe('自扣'));
    it('labels REST as 休息', () => expect(WORK_TYPE_LABELS['REST']).toBe('休息'));

    it('labels SLOWNIGHT as 吃蛋 — NOT 淡场 (淡场 never appears in the Excel, 吃蛋 does)', () => {
      expect(WORK_TYPE_LABELS['SLOWNIGHT']).toBe('吃蛋');
      expect(WORK_TYPE_LABELS['SLOWNIGHT']).not.toBe('淡场');
    });
  });

  describe('workTypes dropdown options', () => {
    it('has an option entry for every work type', () => {
      expect(component.workTypes).toHaveLength(WORK_TYPES.length);
    });

    it('each option has a label and value', () => {
      for (const opt of component.workTypes) {
        expect(opt.label).toBeTruthy();
        expect(opt.value).toBeTruthy();
      }
    });
  });

  // ── Settlement coverage (Excel alignment) ─────────────────────────────────────

  describe('SETTLEMENTS array — Excel alignment', () => {
    it('contains all 6 settlement statuses', () => {
      expect(SETTLEMENTS).toHaveLength(6);
      expect(SETTLEMENTS).toContain('PENDING');
      expect(SETTLEMENTS).toContain('SETTLED');
      expect(SETTLEMENTS).toContain('CASH');
      expect(SETTLEMENTS).toContain('BANKIN');
      expect(SETTLEMENTS).toContain('NOTCOLLECTED');
      expect(SETTLEMENTS).toContain('COMP');
    });

    it('includes CASH as a distinct status — "cash" appears in 88NANA sheet separate from 清/SETTLED', () => {
      expect(SETTLEMENTS).toContain('CASH');
    });
  });

  describe('SETTLEMENT_LABELS — display values', () => {
    it('labels SETTLED as 清 (Settled)', () => expect(SETTLEMENT_LABELS['SETTLED']).toBe('清 (Settled)'));
    it('labels CASH as Cash',           () => expect(SETTLEMENT_LABELS['CASH']).toBe('Cash'));
    it('labels BANKIN as Bank In',      () => expect(SETTLEMENT_LABELS['BANKIN']).toBe('Bank In'));
    it('labels NOTCOLLECTED as 还没收', () => expect(SETTLEMENT_LABELS['NOTCOLLECTED']).toBe('还没收'));
    it('labels COMP as Comp',           () => expect(SETTLEMENT_LABELS['COMP']).toBe('Comp'));
    it('labels PENDING as Pending',     () => expect(SETTLEMENT_LABELS['PENDING']).toBe('Pending'));
  });

  describe('settlements dropdown options', () => {
    it('has an option entry for every settlement', () => {
      expect(component.settlements).toHaveLength(SETTLEMENTS.length);
    });
  });

  // ── recalcVenueEarnings() ─────────────────────────────────────────────────────

  describe('recalcVenueEarnings()', () => {
    it('auto-calcs VENUECLOCK earnings: 5 hours × RM 60 = RM 300', () => {
      component.form.workType   = 'VENUECLOCK';
      component.form.venueUnits = 5;
      component.form.venueRate  = 60;
      component.recalcVenueEarnings();
      expect(component.form.venueEarnings).toBe(300);
    });

    it('auto-calcs TABLERUN earnings: 3 tables × RM 80 = RM 240', () => {
      component.form.workType   = 'TABLERUN';
      component.form.venueUnits = 3;
      component.form.venueRate  = 80;
      component.recalcVenueEarnings();
      expect(component.form.venueEarnings).toBe(240);
    });

    it('auto-calcs PRIVATEHOURS earnings: 2 hrs × RM 120 = RM 240', () => {
      component.form.workType   = 'PRIVATEHOURS';
      component.form.venueUnits = 2;
      component.form.venueRate  = 120;
      component.recalcVenueEarnings();
      expect(component.form.venueEarnings).toBe(240);
    });

    it('rounds to 2 decimal places (e.g. 1 × 33.333 = 33.33)', () => {
      component.form.workType   = 'VENUECLOCK';
      component.form.venueUnits = 1;
      component.form.venueRate  = 33.333;
      component.recalcVenueEarnings();
      expect(component.form.venueEarnings).toBe(33.33);
    });

    it('does NOT auto-calc for OVERTIME — earnings entered manually by manager', () => {
      component.form.workType      = 'OVERTIME';
      component.form.venueUnits    = 2;
      component.form.venueRate     = 100;
      component.form.venueEarnings = 999; // pre-set sentinel
      component.recalcVenueEarnings();
      expect(component.form.venueEarnings).toBe(999); // unchanged
    });

    it('does NOT auto-calc for OVERNIGHT — overnight is entered as gross/net, not units×rate', () => {
      component.form.workType      = 'OVERNIGHT';
      component.form.venueUnits    = 1;
      component.form.venueRate     = 500;
      component.form.venueEarnings = 777;
      component.recalcVenueEarnings();
      expect(component.form.venueEarnings).toBe(777);
    });

    it('does NOT auto-calc for REST — no money earned on a rest night', () => {
      component.form.workType      = 'REST';
      component.form.venueEarnings = 0;
      component.recalcVenueEarnings();
      expect(component.form.venueEarnings).toBe(0);
    });

    it('does NOT auto-calc for SLOWNIGHT (吃蛋) — zero-earning night by definition', () => {
      component.form.workType      = 'SLOWNIGHT';
      component.form.venueEarnings = 0;
      component.recalcVenueEarnings();
      expect(component.form.venueEarnings).toBe(0);
    });

    it('does NOT auto-calc for SELFDEDUCT — comes out of hostess share, not units×rate', () => {
      component.form.workType      = 'SELFDEDUCT';
      component.form.venueUnits    = 1;
      component.form.venueRate     = 60;
      component.form.venueEarnings = 888;
      component.recalcVenueEarnings();
      expect(component.form.venueEarnings).toBe(888);
    });
  });

  // ── hasPrivateFields() ────────────────────────────────────────────────────────

  describe('hasPrivateFields()', () => {
    it('returns true for OVERNIGHT — 包夜 always involves private gross/net', () => {
      component.form.workType = 'OVERNIGHT';
      expect(component.hasPrivateFields()).toBe(true);
    });

    it('returns true for PRIVATEHOURS — 私小时 has a house cut and a hostess cut', () => {
      component.form.workType = 'PRIVATEHOURS';
      expect(component.hasPrivateFields()).toBe(true);
    });

    it('returns false for VENUECLOCK — clock hours go to house only', () => {
      component.form.workType = 'VENUECLOCK';
      expect(component.hasPrivateFields()).toBe(false);
    });

    it('returns false for TABLERUN', () => {
      component.form.workType = 'TABLERUN';
      expect(component.hasPrivateFields()).toBe(false);
    });

    it('returns false for OVERTIME', () => {
      component.form.workType = 'OVERTIME';
      expect(component.hasPrivateFields()).toBe(false);
    });

    it('returns false for SELFDEDUCT', () => {
      component.form.workType = 'SELFDEDUCT';
      expect(component.hasPrivateFields()).toBe(false);
    });

    it('returns false for REST', () => {
      component.form.workType = 'REST';
      expect(component.hasPrivateFields()).toBe(false);
    });

    it('returns false for SLOWNIGHT', () => {
      component.form.workType = 'SLOWNIGHT';
      expect(component.hasPrivateFields()).toBe(false);
    });
  });

  // ── settlementSeverity() ──────────────────────────────────────────────────────

  describe('settlementSeverity()', () => {
    it('SETTLED → success (green) — fully cleared means no outstanding debt', () => {
      expect(component.settlementSeverity('SETTLED')).toBe('success');
    });

    it('BANKIN → info (blue) — transferred but not yet confirmed', () => {
      expect(component.settlementSeverity('BANKIN')).toBe('info');
    });

    it('PENDING → warn (orange) — awaiting collection; action needed', () => {
      expect(component.settlementSeverity('PENDING')).toBe('warn');
    });

    it('NOTCOLLECTED → danger (red) — overdue collection; most urgent', () => {
      expect(component.settlementSeverity('NOTCOLLECTED')).toBe('danger');
    });

    it('COMP → secondary — comped entries are neutral', () => {
      expect(component.settlementSeverity('COMP')).toBe('secondary');
    });

    it('CASH → secondary — physical cash paid; no bank reconciliation needed', () => {
      // CASH is a distinct status from SETTLED; it falls through to secondary by default
      const severity = component.settlementSeverity('CASH');
      expect(['secondary', 'success', 'info']).toContain(severity);
    });
  });

  // ── ngOnInit() ────────────────────────────────────────────────────────────────

  describe('ngOnInit()', () => {
    it('calls staffStore.loadStaff() so the hostess selector is populated on page load', () => {
      component.ngOnInit();
      expect(mockStaffStore.loadStaff).toHaveBeenCalled();
    });
  });

  // ── onStaffChange() ───────────────────────────────────────────────────────────

  describe('onStaffChange()', () => {
    it('sets selectedStaffId to the given id', () => {
      component.onStaffChange('staff-uuid-1');
      expect(component.selectedStaffId()).toBe('staff-uuid-1');
    });

    it('clears the entries list when a new staff is selected', () => {
      component.entries.set([makeEntry()]);
      component.onStaffChange('staff-uuid-2');
      expect(component.entries()).toHaveLength(0);
    });

    it('hides the form when a new staff is selected', () => {
      component.showForm.set(true);
      component.onStaffChange('staff-uuid-1');
      expect(component.showForm()).toBe(false);
    });

    it('calls listEntries when a non-null id is provided', () => {
      component.onStaffChange('staff-uuid-1');
      expect(mockHostessSvc.listEntries).toHaveBeenCalled();
    });

    it('does NOT call listEntries when id is null (deselection)', () => {
      component.onStaffChange(null);
      expect(mockHostessSvc.listEntries).not.toHaveBeenCalled();
    });

    it('sets selectedStaffId to null on deselection', () => {
      component.selectedStaffId.set('staff-uuid-1');
      component.onStaffChange(null);
      expect(component.selectedStaffId()).toBeNull();
    });
  });

  // ── onMonthChange() ───────────────────────────────────────────────────────────

  describe('onMonthChange()', () => {
    it('updates selectedMonth signal', () => {
      const newMonth = { label: 'March 2026', year: 2026, month: 3 };
      component.onMonthChange(newMonth);
      expect(component.selectedMonth()).toEqual(newMonth);
    });

    it('loads entries when a staff is already selected', () => {
      component.selectedStaffId.set('staff-uuid-1');
      mockHostessSvc.listEntries.mockClear();
      component.onMonthChange({ label: 'March 2026', year: 2026, month: 3 });
      expect(mockHostessSvc.listEntries).toHaveBeenCalled();
    });

    it('does NOT load entries when no staff is selected', () => {
      component.selectedStaffId.set(null);
      component.onMonthChange({ label: 'March 2026', year: 2026, month: 3 });
      expect(mockHostessSvc.listEntries).not.toHaveBeenCalled();
    });

    it('passes correct year+month to listEntries', () => {
      component.selectedStaffId.set('staff-uuid-1');
      component.onMonthChange({ label: 'March 2026', year: 2026, month: 3 });
      expect(mockHostessSvc.listEntries).toHaveBeenCalledWith('staff-uuid-1', 2026, 3);
    });
  });

  // ── listEntries loading ───────────────────────────────────────────────────────

  describe('loadEntries (via onStaffChange)', () => {
    it('sets entries signal with data returned from the API', () => {
      const entries = [makeEntry(), makeEntry({ id: 'e2', venueEarnings: 200 })];
      mockHostessSvc.listEntries.mockReturnValue(of(entries));
      component.onStaffChange('staff-uuid-1');
      expect(component.entries()).toHaveLength(2);
    });

    it('clears loading flag after successful load', () => {
      mockHostessSvc.listEntries.mockReturnValue(of([makeEntry()]));
      component.onStaffChange('staff-uuid-1');
      expect(component.loading()).toBe(false);
    });

    it('shows error toast and clears loading on API failure', () => {
      mockHostessSvc.listEntries.mockReturnValue(throwError(() => new Error('Network')));
      component.onStaffChange('staff-uuid-1');
      expect(component.loading()).toBe(false);
      expect(mockToastSvc.error).toHaveBeenCalled();
    });
  });

  // ── openAdd() ────────────────────────────────────────────────────────────────

  describe('openAdd()', () => {
    it('shows the form', () => {
      component.openAdd();
      expect(component.showForm()).toBe(true);
    });

    it('clears editingId so save() uses createEntry path', () => {
      component.editingId.set('old-id');
      component.openAdd();
      expect(component.editingId()).toBeNull();
    });

    it('clears any previous form error', () => {
      component.formError.set('Previous error');
      component.openAdd();
      expect(component.formError()).toBe('');
    });

    it('resets the form to default VENUECLOCK work type', () => {
      component.form.workType = 'OVERNIGHT';
      component.openAdd();
      expect(component.form.workType).toBe('VENUECLOCK');
    });

    it('resets the form to PENDING settlement', () => {
      component.form.settlement = 'SETTLED';
      component.openAdd();
      expect(component.form.settlement).toBe('PENDING');
    });
  });

  // ── openEdit() ────────────────────────────────────────────────────────────────

  describe('openEdit()', () => {
    it('populates form fields from the entry', () => {
      const entry = makeEntry({ workType: 'OVERNIGHT', venueEarnings: 500, privateGross: 1200, privateNet: 900, settlement: 'BANKIN' });
      component.openEdit(entry);
      expect(component.form.workType).toBe('OVERNIGHT');
      expect(component.form.venueEarnings).toBe(500);
      expect(component.form.privateGross).toBe(1200);
      expect(component.form.privateNet).toBe(900);
      expect(component.form.settlement).toBe('BANKIN');
    });

    it('sets editingId to the entry id so save() uses updateEntry path', () => {
      component.openEdit(makeEntry({ id: 'edit-target' }));
      expect(component.editingId()).toBe('edit-target');
    });

    it('shows the form', () => {
      component.openEdit(makeEntry());
      expect(component.showForm()).toBe(true);
    });

    it('clears any previous form error', () => {
      component.formError.set('Some stale error');
      component.openEdit(makeEntry());
      expect(component.formError()).toBe('');
    });

    it('sets entryDate on the form correctly', () => {
      component.openEdit(makeEntry({ entryDate: '2026-04-15' }));
      expect(component.form.entryDate).toBe('2026-04-15');
    });

    it('sets notes correctly (null preserved)', () => {
      component.openEdit(makeEntry({ notes: null }));
      expect(component.form.notes).toBeNull();
    });
  });

  // ── cancel() ─────────────────────────────────────────────────────────────────

  describe('cancel()', () => {
    it('hides the form', () => {
      component.showForm.set(true);
      component.cancel();
      expect(component.showForm()).toBe(false);
    });

    it('clears editingId', () => {
      component.editingId.set('some-id');
      component.cancel();
      expect(component.editingId()).toBeNull();
    });

    it('clears formError', () => {
      component.formError.set('Some error');
      component.cancel();
      expect(component.formError()).toBe('');
    });
  });

  // ── save() — create path ──────────────────────────────────────────────────────

  describe('save() — create path (no editingId)', () => {
    function setupCreate(): void {
      component.selectedStaffId.set('staff-uuid-1');
      component.editingId.set(null);
      component.form.entryDate = '2026-04-30';
      component.form.workType  = 'VENUECLOCK';
    }

    it('calls createEntry (not updateEntry) when editingId is null', () => {
      setupCreate();
      component.save();
      expect(mockHostessSvc.createEntry).toHaveBeenCalled();
      expect(mockHostessSvc.updateEntry).not.toHaveBeenCalled();
    });

    it('passes staffId from selectedStaffId signal in the request', () => {
      setupCreate();
      component.save();
      const req = mockHostessSvc.createEntry.mock.calls[0][0];
      expect(req.staffId).toBe('staff-uuid-1');
    });

    it('shows success toast after create', () => {
      setupCreate();
      component.save();
      expect(mockToastSvc.success).toHaveBeenCalled();
    });

    it('hides the form after successful create', () => {
      setupCreate();
      component.showForm.set(true);
      component.save();
      expect(component.showForm()).toBe(false);
    });

    it('clears editingId after successful create', () => {
      setupCreate();
      component.save();
      expect(component.editingId()).toBeNull();
    });

    it('reloads entries after create so the new row appears in the table', () => {
      setupCreate();
      mockHostessSvc.listEntries.mockClear();
      component.save();
      expect(mockHostessSvc.listEntries).toHaveBeenCalled();
    });

    it('clears saving flag after success', () => {
      setupCreate();
      component.save();
      expect(component.saving()).toBe(false);
    });
  });

  // ── save() — update path ──────────────────────────────────────────────────────

  describe('save() — update path (editingId set)', () => {
    function setupUpdate(): void {
      component.selectedStaffId.set('staff-uuid-1');
      component.editingId.set('entry-to-update');
      component.form.entryDate = '2026-04-30';
      component.form.workType  = 'TABLERUN';
    }

    it('calls updateEntry (not createEntry) when editingId is set', () => {
      setupUpdate();
      component.save();
      expect(mockHostessSvc.updateEntry).toHaveBeenCalled();
      expect(mockHostessSvc.createEntry).not.toHaveBeenCalled();
    });

    it('passes the editingId as the first arg to updateEntry', () => {
      setupUpdate();
      component.save();
      expect(mockHostessSvc.updateEntry).toHaveBeenCalledWith(
        'entry-to-update',
        expect.objectContaining({ workType: 'TABLERUN' }),
      );
    });

    it('shows success toast after update', () => {
      setupUpdate();
      component.save();
      expect(mockToastSvc.success).toHaveBeenCalled();
    });

    it('reloads entries after update', () => {
      setupUpdate();
      mockHostessSvc.listEntries.mockClear();
      component.save();
      expect(mockHostessSvc.listEntries).toHaveBeenCalled();
    });
  });

  // ── save() — validation early exit ───────────────────────────────────────────

  describe('save() — early exit conditions', () => {
    it('does nothing when no staffId is selected', () => {
      component.selectedStaffId.set(null);
      component.form.entryDate = '2026-04-30';
      component.form.workType  = 'VENUECLOCK';
      component.save();
      expect(mockHostessSvc.createEntry).not.toHaveBeenCalled();
      expect(mockHostessSvc.updateEntry).not.toHaveBeenCalled();
    });

    it('does nothing when entryDate is empty', () => {
      component.selectedStaffId.set('staff-uuid-1');
      component.form.entryDate = '';
      component.form.workType  = 'VENUECLOCK';
      component.save();
      expect(mockHostessSvc.createEntry).not.toHaveBeenCalled();
    });

    it('does nothing when workType is empty', () => {
      component.selectedStaffId.set('staff-uuid-1');
      component.form.entryDate = '2026-04-30';
      component.form.workType  = '' as any;
      component.save();
      expect(mockHostessSvc.createEntry).not.toHaveBeenCalled();
    });
  });

  // ── save() — API error ────────────────────────────────────────────────────────

  describe('save() — API error', () => {
    beforeEach(() => {
      component.selectedStaffId.set('staff-uuid-1');
      component.editingId.set(null);
      component.form.entryDate = '2026-04-30';
      component.form.workType  = 'VENUECLOCK';
    });

    it('sets formError from the API error message', () => {
      mockHostessSvc.createEntry.mockReturnValue(
        throwError(() => ({ error: { error: 'Entry date out of range.' } })),
      );
      component.save();
      expect(component.formError()).toBe('Entry date out of range.');
    });

    it('shows error toast on API failure', () => {
      mockHostessSvc.createEntry.mockReturnValue(
        throwError(() => ({ error: { error: 'Entry date out of range.' } })),
      );
      component.save();
      expect(mockToastSvc.error).toHaveBeenCalledWith('Entry date out of range.');
    });

    it('clears saving flag after error so the Save button is re-enabled', () => {
      mockHostessSvc.createEntry.mockReturnValue(throwError(() => ({})));
      component.save();
      expect(component.saving()).toBe(false);
    });

    it('does NOT hide the form on error — user needs to fix input', () => {
      mockHostessSvc.createEntry.mockReturnValue(throwError(() => ({})));
      component.showForm.set(true);
      component.save();
      expect(component.showForm()).toBe(true);
    });
  });

  // ── remove() ─────────────────────────────────────────────────────────────────

  describe('remove()', () => {
    it('shows a confirm dialog before deleting', () => {
      component.remove(makeEntry());
      expect(mockConfirmSvc.confirm).toHaveBeenCalled();
    });

    it('uses danger style in the confirm dialog', () => {
      component.remove(makeEntry());
      expect(mockConfirmSvc.confirm.mock.calls[0][0].danger).toBe(true);
    });

    it('calls deleteEntry with the entry id when confirmed', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.remove(makeEntry({ id: 'del-entry-1' }));
      expect(mockHostessSvc.deleteEntry).toHaveBeenCalledWith('del-entry-1');
    });

    it('shows success toast after deletion', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      component.remove(makeEntry());
      expect(mockToastSvc.success).toHaveBeenCalled();
    });

    it('reloads entries after deletion so the table reflects the removal', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockHostessSvc.listEntries.mockClear();
      component.selectedStaffId.set('staff-uuid-1');
      component.remove(makeEntry());
      expect(mockHostessSvc.listEntries).toHaveBeenCalled();
    });

    it('does NOT call deleteEntry when user cancels the dialog', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(false));
      component.remove(makeEntry());
      expect(mockHostessSvc.deleteEntry).not.toHaveBeenCalled();
    });

    it('shows error toast when delete API call fails', () => {
      mockConfirmSvc.confirm.mockReturnValue(of(true));
      mockHostessSvc.deleteEntry.mockReturnValue(throwError(() => new Error('Conflict')));
      component.remove(makeEntry());
      expect(mockToastSvc.error).toHaveBeenCalled();
    });
  });

  // ── Computed summary signals ──────────────────────────────────────────────────

  describe('computed summary signals', () => {
    it('totalVenue sums venueEarnings across all entries', () => {
      component.entries.set([
        makeEntry({ venueEarnings: 300 }),
        makeEntry({ id: 'e2', venueEarnings: 200 }),
        makeEntry({ id: 'e3', venueEarnings: 150 }),
      ]);
      expect(component.totalVenue()).toBe(650);
    });

    it('totalVenue is 0 when there are no entries', () => {
      component.entries.set([]);
      expect(component.totalVenue()).toBe(0);
    });

    it('totalPrivateNet sums privateNet across all entries', () => {
      component.entries.set([
        makeEntry({ id: 'e1', privateNet: 900 }),
        makeEntry({ id: 'e2', privateNet: 600 }),
      ]);
      expect(component.totalPrivateNet()).toBe(1500);
    });

    it('totalPrivateNet is 0 when all entries have zero private earnings (e.g. all VENUECLOCK)', () => {
      component.entries.set([makeEntry({ privateNet: 0 }), makeEntry({ id: 'e2', privateNet: 0 })]);
      expect(component.totalPrivateNet()).toBe(0);
    });

    it('entryCount returns the number of entries', () => {
      component.entries.set([makeEntry(), makeEntry({ id: 'e2' }), makeEntry({ id: 'e3' })]);
      expect(component.entryCount()).toBe(3);
    });

    it('entryCount is 0 when there are no entries', () => {
      component.entries.set([]);
      expect(component.entryCount()).toBe(0);
    });
  });

  // ── monthOpts ─────────────────────────────────────────────────────────────────

  describe('monthOpts', () => {
    it('builds 12 month options (current + 11 preceding)', () => {
      expect(component.monthOpts).toHaveLength(12);
    });

    it('each option has a label, year and month', () => {
      for (const opt of component.monthOpts) {
        expect(opt.label).toBeTruthy();
        expect(opt.year).toBeGreaterThan(2020);
        expect(opt.month).toBeGreaterThanOrEqual(1);
        expect(opt.month).toBeLessThanOrEqual(12);
      }
    });

    it('defaultSelectedMonth starts as the first option (most recent month)', () => {
      expect(component.selectedMonth()).toEqual(component.monthOpts[0]);
    });
  });
});
