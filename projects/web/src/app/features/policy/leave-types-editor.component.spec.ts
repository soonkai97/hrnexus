import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { LeaveTypeCatalogue, LeaveTypeDefinition, LeaveTypeService, ToastService } from 'shared-lib';
import { LeaveTypesEditorComponent } from './leave-types-editor.component';

/**
 * WHY these tests matter: the leave types decide who is charged what, so the editor must refuse what the
 * server refuses (so the person sees why before saving), must never let a type that already has leave change
 * where it is charged or be removed (that would rewrite history), and a save from a stale page must never
 * silently overwrite a colleague's change.
 */
describe('LeaveTypesEditorComponent', () => {
  let component: LeaveTypesEditorComponent;
  let mockTypes: { loadAll: ReturnType<typeof vi.fn>; save: ReturnType<typeof vi.fn> };
  let mockToast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  const type = (changes: Partial<LeaveTypeDefinition>): LeaveTypeDefinition => ({
    key: 'study',
    label: 'Study',
    colour: 'info',
    sortOrder: 100,
    active: true,
    isSystem: false,
    pool: 'none',
    allowanceDays: null,
    maxDaysPerRequest: null,
    autoApprove: false,
    advanceNoticeDays: null,
    advanceNoticeFromPolicy: false,
    deductsPay: false,
    adminOnly: false,
    countsCalendarDays: false,
    sensitive: false,
    hasLeave: false,
    ...changes,
  });

  const sick = type({ key: 'sick', label: 'Sick', pool: 'sick', autoApprove: true, isSystem: true, sortOrder: 10, hasLeave: true });
  const noShow = type({ key: 'no-show', label: 'No-show', adminOnly: true, deductsPay: true, isSystem: true, sortOrder: 20 });
  const annual = type({ key: 'annual', label: 'Annual', pool: 'annual', advanceNoticeFromPolicy: true, sortOrder: 5, hasLeave: true });
  const unpaid = type({ key: 'unpaid', label: 'Unpaid', deductsPay: true, sortOrder: 30 });

  const catalogue = (types: LeaveTypeDefinition[], version = 3): LeaveTypeCatalogue => ({ version, isSaved: true, types });

  async function make(initial = catalogue([sick, noShow, annual, unpaid])) {
    mockTypes = {
      loadAll: vi.fn().mockReturnValue(of(initial)),
      save: vi.fn().mockReturnValue(of(4)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [LeaveTypesEditorComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: LeaveTypeService, useValue: mockTypes },
        { provide: ToastService, useValue: mockToast },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(LeaveTypesEditorComponent);
    component = fixture.componentInstance;
    // Do NOT call detectChanges() — avoids PrimeNG template initialisation errors.
    component.ngOnInit();
  }

  // ── loading ───────────────────────────────────────────────────────────────────

  it('loads every type in sort order and starts with nothing unsaved', async () => {
    await make();
    expect(component.rows().map((r) => r.key)).toEqual(['annual', 'sick', 'no-show', 'unpaid']);
    expect(component.loading()).toBe(false);
    expect(component.hasUnsavedChanges()).toBe(false);
  });

  it('says so when the types cannot be loaded', async () => {
    await make();
    mockTypes.loadAll.mockReturnValue(throwError(() => ({ status: 500, error: { error: 'boom' } })));
    component.load();
    expect(component.error()).toBe('boom');
    expect(component.loading()).toBe(false);
  });

  // ── describing a type ─────────────────────────────────────────────────────────

  it('describes the balance and limit of a type in words', async () => {
    await make();
    expect(component.balanceText(annual)).toBe('Annual balance');
    expect(component.balanceText(sick)).toBe('Sick balance');
    expect(component.balanceText(type({ pool: 'own', allowanceDays: 60 }))).toBe('60 days a year');
    expect(component.balanceText(unpaid)).toBe('No balance');
    expect(component.limitText(unpaid)).toBe('Company limit');
    expect(component.limitText(type({ maxDaysPerRequest: 98, countsCalendarDays: true }))).toBe('Up to 98 calendar days');
  });

  it('lists the flags that change how a type behaves', async () => {
    await make();
    expect(component.flags(sick)).toEqual(['Approves itself']);
    expect(component.flags(annual)).toEqual(['Company notice']);
    expect(component.flags(type({ advanceNoticeDays: 10, sensitive: true, adminOnly: true }))).toEqual(['10 days notice', 'Admin only', 'Private']);
  });

  // ── adding ────────────────────────────────────────────────────────────────────

  it('a new type starts blank, last in the order, and needs a valid key and name', async () => {
    await make();
    component.startAdd();
    expect(component.draft.editingKey).toBeNull();
    expect(component.draft.sortOrder).toBe(40);   // 30 + 10

    expect(component.draftError()).toContain('key');
    component.draft.key = 'Study Leave';
    expect(component.draftError()).toContain('key');
    component.draft.key = 'study-leave';
    expect(component.draftError()).toContain('name');
    component.draft.label = 'Study leave';
    expect(component.draftError()).toBe('');
  });

  it('refuses a key that already exists', async () => {
    await make();
    component.startAdd();
    component.draft.key = 'unpaid';
    component.draft.label = 'Another';
    expect(component.draftError()).toContain("'unpaid' already exists");
  });

  it('done adds the type to the list in order and marks the page as having unsaved changes', async () => {
    await make();
    component.startAdd();
    component.draft.key = 'study';
    component.draft.label = 'Study';
    component.draft.sortOrder = 15;
    component.applyDraft();

    expect(component.rows().map((r) => r.key)).toEqual(['annual', 'sick', 'study', 'no-show', 'unpaid']);
    expect(component.hasUnsavedChanges()).toBe(true);
    expect(component.dialogOpen()).toBe(false);
  });

  it('keeps the dialog open and says what is wrong when the draft is invalid', async () => {
    await make();
    component.startAdd();
    component.applyDraft();
    expect(component.dialogOpen()).toBe(true);
    expect(component.dialogError()).not.toBe('');
  });

  // ── the same rules the server enforces ────────────────────────────────────────

  it('a type with its own pool needs a yearly allowance', async () => {
    await make();
    component.startAdd();
    component.draft.key = 'hosp';
    component.draft.label = 'Hosp';
    component.draft.pool = 'own';
    expect(component.draftError()).toContain('allowance');
    component.draft.allowanceDays = 60;
    expect(component.draftError()).toBe('');
  });

  it('a type with no balance cannot approve itself: a long absence needs a manager', async () => {
    await make();
    component.startAdd();
    component.draft.key = 'long';
    component.draft.label = 'Long';
    component.draft.autoApprove = true;
    expect(component.draftError()).toContain('cannot approve itself');
    component.draft.pool = 'sick';
    expect(component.draftError()).toBe('');
    component.draft.adminOnly = true;
    expect(component.draftError()).toContain('administrator');
  });

  it('keeps day limits and notice within the server bounds', async () => {
    await make();
    component.startAdd();
    component.draft.key = 'lim';
    component.draft.label = 'Lim';
    component.draft.maxDaysPerRequest = 400;
    expect(component.draftError()).toContain('longest single request');
    component.draft.maxDaysPerRequest = 98;
    component.draft.noticeMode = 'days';
    component.draft.noticeDays = 90;
    expect(component.draftError()).toContain('Notice');
    component.draft.noticeDays = 10;
    expect(component.draftError()).toBe('');
  });

  // ── editing locks ─────────────────────────────────────────────────────────────

  it('where a type is charged is locked once it has leave, and fixed for a system type', async () => {
    await make();
    component.startEdit(annual);   // has leave
    expect(component.poolLocked()).toBe(true);
    expect(component.behaviourLocked()).toBe(false);

    component.startEdit(sick);     // system
    expect(component.poolLocked()).toBe(true);
    expect(component.behaviourLocked()).toBe(true);

    component.startEdit(unpaid);   // no leave, not system
    expect(component.poolLocked()).toBe(false);
    expect(component.behaviourLocked()).toBe(false);
  });

  it('editing keeps the type key and turns the notice choice back into the saved fields', async () => {
    await make();
    component.startEdit(annual);
    expect(component.draft.noticeMode).toBe('policy');
    component.draft.label = 'Annual leave';
    component.draft.noticeMode = 'days';
    component.draft.noticeDays = 7;
    component.applyDraft();

    const saved = component.rows().find((r) => r.key === 'annual')!;
    expect(saved.label).toBe('Annual leave');
    expect(saved.advanceNoticeDays).toBe(7);
    expect(saved.advanceNoticeFromPolicy).toBe(false);
  });

  it('a system type stays active even if the draft says otherwise', async () => {
    await make();
    component.startEdit(noShow);
    component.draft.active = false;
    component.applyDraft();
    expect(component.rows().find((r) => r.key === 'no-show')!.active).toBe(true);
  });

  // ── turning off and removing ──────────────────────────────────────────────────

  it('a system type cannot be turned off or removed', async () => {
    await make();
    expect(component.canToggle(sick)).toBe(false);
    expect(component.canRemove(sick)).toBe(false);
    component.toggleActive(sick);
    expect(component.rows().find((r) => r.key === 'sick')!.active).toBe(true);
  });

  it('a type with leave can be turned off but never removed', async () => {
    await make();
    expect(component.canRemove(annual)).toBe(false);
    component.toggleActive(annual);
    expect(component.rows().find((r) => r.key === 'annual')!.active).toBe(false);
    component.remove(annual);
    expect(component.rows().some((r) => r.key === 'annual')).toBe(true);
  });

  it('a type without leave can be removed', async () => {
    await make();
    expect(component.canRemove(unpaid)).toBe(true);
    component.remove(unpaid);
    expect(component.rows().some((r) => r.key === 'unpaid')).toBe(false);
  });

  it('the catalogue needs at least one active type that staff can apply for', async () => {
    await make(catalogue([sick, noShow]));
    expect(component.catalogueError()).toBe('');   // sick is active and open to staff
    component.rows.set([noShow, { ...sick, active: false }]);
    expect(component.catalogueError()).toContain('open to staff');
  });

  // ── saving ────────────────────────────────────────────────────────────────────

  it('saving sends the whole catalogue with the loaded version and reloads the usage flags', async () => {
    await make();
    component.toggleActive(unpaid);
    component.save();

    expect(mockTypes.save).toHaveBeenCalledTimes(1);
    const [version, rows] = mockTypes.save.mock.calls[0] as [number, LeaveTypeDefinition[]];
    expect(version).toBe(3);
    expect(rows.find((r) => r.key === 'unpaid')!.active).toBe(false);
    expect(mockToast.success).toHaveBeenCalled();
    expect(mockTypes.loadAll).toHaveBeenCalledTimes(2);   // once on open, once after the save
  });

  it('does not save a catalogue the server would refuse', async () => {
    await make(catalogue([noShow, { ...sick, active: false, isSystem: false }]));
    component.save();
    expect(mockTypes.save).not.toHaveBeenCalled();
    expect(component.error()).toContain('open to staff');
  });

  it('a stale save shows the server message and offers to reload instead of overwriting', async () => {
    await make();
    mockTypes.save.mockReturnValue(throwError(() => ({ status: 409, error: { error: 'changed by someone else' } })));
    component.toggleActive(unpaid);
    component.save();

    expect(component.conflict()).toBe(true);
    expect(component.error()).toBe('changed by someone else');
    expect(component.saving()).toBe(false);
  });

  it('discarding takes whatever is saved now', async () => {
    await make();
    component.remove(unpaid);
    expect(component.hasUnsavedChanges()).toBe(true);
    component.load();
    expect(component.hasUnsavedChanges()).toBe(false);
    expect(component.rows().some((r) => r.key === 'unpaid')).toBe(true);
  });
});
