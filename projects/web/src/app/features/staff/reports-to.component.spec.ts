import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { MessageService, ConfirmationService } from 'primeng/api';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { StaffManagementService, ToastService } from 'shared-lib';
import { ReportsToComponent } from './reports-to.component';

/**
 * Who a person reports to.
 *
 * WHY these tests matter: the reporting line decides whose pay a manager may read and sign off. The editor must never offer a
 * person as their own manager or an inactive person, must keep the current manager selectable while searching, must save
 * only a real change, and must show the server's reason when a change is refused (a loop, or the person's pay being in a run
 * under review).
 */
describe('ReportsToComponent', () => {
  let component: ReportsToComponent;
  let svc: { setReportsTo: ReturnType<typeof vi.fn>; getAll: ReturnType<typeof vi.fn> };
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  const person = (id: string, active = true) => ({
    id,
    staffId: id.toUpperCase(),
    fullName: `Person ${id}`,
    isActive: active,
  });

  beforeEach(async () => {
    vi.useFakeTimers();
    svc = {
      setReportsTo: vi.fn().mockReturnValue(of(void 0)),
      getAll: vi
        .fn()
        .mockReturnValue(
          of({ items: [person('b'), person('c', false), person('self')], totalCount: 3 }),
        ),
    };
    toast = { success: vi.fn(), error: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [ReportsToComponent],
      providers: [
        provideHttpClient(),
        { provide: StaffManagementService, useValue: svc },
        { provide: ToastService, useValue: toast },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
    component = TestBed.createComponent(ReportsToComponent).componentInstance;
    component.staffId = 'self';
    component.currentId = 'a';
    component.currentName = 'A — Person a';
    component.canManage = true;
    component.ngOnChanges();
  });

  afterEach(() => vi.useRealTimers());

  it('starts on the current manager, selectable before any search has answered', () => {
    expect(component.managerId).toBe('a');
    expect(component.changed).toBe(false);
    expect(component.options()).toEqual([{ label: 'A — Person a', value: 'a' }]);
  });

  it('searches the server and offers active people other than the person themselves, keeping the current manager', () => {
    vi.advanceTimersByTime(300);

    expect(svc.getAll).toHaveBeenCalledWith('', 1, 20);
    expect(component.options().map((o) => o.value)).toEqual(['a', 'b']);
  });

  it('searches again as the person types, after a short pause', () => {
    vi.advanceTimersByTime(300);
    svc.getAll.mockClear();

    component.search('  ali ');
    component.search('alic');
    vi.advanceTimersByTime(300);

    expect(svc.getAll).toHaveBeenCalledTimes(1);
    expect(svc.getAll).toHaveBeenCalledWith('alic', 1, 20);
  });

  it('does not search for someone who cannot change it', () => {
    vi.advanceTimersByTime(300); // the search the editor queued when it opened
    svc.getAll.mockClear();
    component.canManage = false;
    component.ngOnChanges();
    vi.advanceTimersByTime(300);

    expect(svc.getAll).not.toHaveBeenCalled();
  });

  it('saves a change and clears with nobody', () => {
    vi.advanceTimersByTime(300);
    component.managerId = 'b';
    expect(component.changed).toBe(true);
    component.save();
    expect(svc.setReportsTo).toHaveBeenCalledWith('self', 'b');
    expect(component.currentId).toBe('b');
    expect(toast.success).toHaveBeenCalledWith('Saved.');

    component.managerId = null;
    component.save();
    expect(svc.setReportsTo).toHaveBeenLastCalledWith('self', null);
  });

  it('does not call the server when nothing changed', () => {
    component.save();

    expect(svc.setReportsTo).not.toHaveBeenCalled();
  });

  it('shows the server reason when a change is refused and keeps the person on the old manager', () => {
    svc.setReportsTo.mockReturnValue(
      throwError(() => ({
        error: { error: 'That would put someone above themselves in the reporting line.' },
      })),
    );
    component.managerId = 'b';

    component.save();

    expect(toast.error).toHaveBeenCalledWith(
      'That would put someone above themselves in the reporting line.',
    );
    expect(component.currentId).toBe('a');
    expect(component.saving()).toBe(false);
  });

  it('says so when the search fails', () => {
    svc.getAll.mockReturnValue(throwError(() => new Error('x')));

    component.search('zed');
    vi.advanceTimersByTime(300);

    expect(toast.error).toHaveBeenCalledWith('Failed to search the staff list.');
  });
});
