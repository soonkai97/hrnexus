import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { MessageService, ConfirmationService } from 'primeng/api';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { StaffManagementService, StaffStore, ToastService } from 'shared-lib';
import { ReportsToComponent } from './reports-to.component';

/**
 * Who a person reports to.
 *
 * WHY these tests matter: the reporting line decides whose pay a manager may read and sign off. The editor must never offer a
 * person as their own manager or an inactive person, must save only a real change, and must show the server's reason when a
 * change is refused (a loop, or the person's pay being in a run under review).
 */
describe('ReportsToComponent', () => {
  let component: ReportsToComponent;
  let svc: { setReportsTo: ReturnType<typeof vi.fn> };
  let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  const person = (id: string, active = true) => ({ id, staffId: id.toUpperCase(), fullName: `Person ${id}`, isActive: active });

  beforeEach(async () => {
    svc = { setReportsTo: vi.fn().mockReturnValue(of(void 0)) };
    toast = { success: vi.fn(), error: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [ReportsToComponent],
      providers: [
        provideHttpClient(),
        { provide: StaffManagementService, useValue: svc },
        { provide: StaffStore, useValue: { staff: () => [person('a'), person('b'), person('c', false), person('self')] } },
        { provide: ToastService, useValue: toast },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
    component = TestBed.createComponent(ReportsToComponent).componentInstance;
    component.staffId = 'self';
    component.currentId = 'a';
    component.canManage = true;
    component.ngOnChanges();
  });

  it('starts on the current manager', () => {
    expect(component.managerId).toBe('a');
    expect(component.changed).toBe(false);
  });

  it('offers active people other than the person themselves', () => {
    expect(component.options().map((o) => o.value)).toEqual(['a', 'b']);
  });

  it('saves a change and clears with nobody', () => {
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
    svc.setReportsTo.mockReturnValue(throwError(() => ({ error: { error: 'That would put someone above themselves in the reporting line.' } })));
    component.managerId = 'b';

    component.save();

    expect(toast.error).toHaveBeenCalledWith('That would put someone above themselves in the reporting line.');
    expect(component.currentId).toBe('a');
    expect(component.saving()).toBe(false);
  });
});
