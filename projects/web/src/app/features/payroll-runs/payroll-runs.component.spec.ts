import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { PayrollRunService, PayrollRunSummary, PermissionService, ToastService } from 'shared-lib';
import { PayrollRunsComponent } from './payroll-runs.component';

/**
 * The list of payroll runs.
 *
 * WHY these tests matter: this is where a month is started. Only someone who manages payroll is offered the button, a run
 * that cannot be started must say why (another run is still open, nobody to pay), and the sign-off progress must read correctly
 * because it is how the head of HR sees what is still waiting.
 */
describe('PayrollRunsComponent', () => {
  let component: PayrollRunsComponent;
  let svc: { list: ReturnType<typeof vi.fn>; start: ReturnType<typeof vi.fn> };
  let toast: { error: ReturnType<typeof vi.fn> };
  let manage = false;

  const run = (over: Partial<PayrollRunSummary> = {}): PayrollRunSummary => ({
    id: 'r1', year: 2026, month: 10, state: 'draft', version: 1, people: 4, leftOut: 0, signedOff: 0,
    totalGross: 1, totalNet: 1, totalEmployerCost: 1, createdAt: 'x', exportedAt: null, ...over,
  });

  beforeEach(async () => {
    manage = false;
    svc = { list: vi.fn().mockReturnValue(of([run()])), start: vi.fn().mockReturnValue(of({ id: 'new' })) };
    toast = { error: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [PayrollRunsComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: PayrollRunService, useValue: svc },
        { provide: PermissionService, useValue: { has: (k: string) => manage && k === 'payroll.manage' } },
        { provide: ToastService, useValue: toast },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
    component = TestBed.createComponent(PayrollRunsComponent).componentInstance;
  });

  it('loads the runs', () => {
    component.ngOnInit();

    expect(component.runs()).toHaveLength(1);
    expect(component.loading()).toBe(false);
    expect(component.empty()).toBe(false);
  });

  it('says so when there are no runs', () => {
    svc.list.mockReturnValue(of([]));

    component.ngOnInit();

    expect(component.empty()).toBe(true);
  });

  it('shows the server reason when the list cannot be loaded', () => {
    svc.list.mockReturnValue(throwError(() => ({ error: { error: 'You are not allowed to see this.' } })));

    component.ngOnInit();

    expect(toast.error).toHaveBeenCalledWith('You are not allowed to see this.');
  });

  it('only someone who manages payroll is offered the Start button', () => {
    expect(component.canStart).toBe(false);
    manage = true;
    expect(component.canStart).toBe(true);
  });

  it('reads the sign-off progress, and shows nothing for a draft', () => {
    expect(component.progress(run({ state: 'draft' }))).toBe('—');
    expect(component.progress(run({ state: 'submitted', people: 4, signedOff: 3 }))).toBe('3 of 4 signed');
  });

  it('opens the start dialog on last month', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 14));   // 14 November 2026
    component.openStart();
    vi.useRealTimers();

    expect([component.startYear, component.startMonth]).toEqual([2026, 10]);
    expect(component.showStart()).toBe(true);
  });

  it('January opens on December of the year before', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2027, 0, 5));
    component.openStart();
    vi.useRealTimers();

    expect([component.startYear, component.startMonth]).toEqual([2026, 12]);
  });

  it('starting a month goes to the new run', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.startYear = 2026;
    component.startMonth = 10;

    component.start();

    expect(svc.start).toHaveBeenCalledWith(2026, 10);
    expect(navigate).toHaveBeenCalledWith(['/payroll-runs', 'new']);
    expect(component.showStart()).toBe(false);
  });

  it('a refused start shows the reason and keeps the dialog open', () => {
    svc.start.mockReturnValue(throwError(() => ({ error: { error: 'The 2026-09 payroll run is not locked yet.' } })));
    component.showStart.set(true);

    component.start();

    expect(toast.error).toHaveBeenCalledWith('The 2026-09 payroll run is not locked yet.');
    expect(component.showStart()).toBe(true);
    expect(component.starting()).toBe(false);
  });

  it('refuses an impossible month before asking the server', () => {
    component.startMonth = 13;

    component.start();

    expect(component.isStartValid).toBe(false);
    expect(svc.start).not.toHaveBeenCalled();
  });
});
