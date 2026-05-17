import { TestBed } from '@angular/core/testing';
import { ConfirmationService } from 'primeng/api';
import { firstValueFrom } from 'rxjs';
import { ConfirmService } from './confirm.service';

/**
 * ConfirmService — wraps PrimeNG ConfirmationService for a simpler Observable API.
 *
 * WHY these tests matter:
 *  - Every destructive action (delete staff, close payroll period) goes through
 *    confirm() — a bug here silently allows unconfirmed deletions.
 *  - accept() and reject() callbacks must both complete the Subject so callers
 *    don't leak subscriptions waiting for a result that never arrives.
 *  - The `danger` option controls button severity — wrong severity makes destructive
 *    actions look safe (primary blue instead of danger red).
 */
describe('ConfirmService', () => {
  let service: ConfirmService;
  let primeSvc: ConfirmationService;

  // Capture the last confirmation config so tests can invoke accept/reject
  let capturedConfig: any;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ConfirmationService],
    });
    service  = TestBed.inject(ConfirmService);
    primeSvc = TestBed.inject(ConfirmationService);

    vi.spyOn(primeSvc, 'confirm').mockImplementation((config: any) => {
      capturedConfig = config;
      return primeSvc; // ConfirmationService.confirm() returns `this` for chaining
    });
  });

  afterEach(() => vi.restoreAllMocks());

  // ── accept path ───────────────────────────────────────────────────────────

  it('resolves true when the user accepts', async () => {
    const promise = firstValueFrom(service.confirm({ message: 'Delete this?' }));
    capturedConfig.accept();
    expect(await promise).toBe(true);
  });

  // ── reject path ───────────────────────────────────────────────────────────

  it('resolves false when the user rejects', async () => {
    const promise = firstValueFrom(service.confirm({ message: 'Delete this?' }));
    capturedConfig.reject();
    expect(await promise).toBe(false);
  });

  // ── Observable completes ──────────────────────────────────────────────────

  it('the returned Observable completes after accept so subscriptions are not leaked', async () => {
    let completed = false;
    service.confirm({ message: 'Confirm?' }).subscribe({ complete: () => { completed = true; } });
    capturedConfig.accept();
    expect(completed).toBe(true);
  });

  it('the returned Observable completes after reject so subscriptions are not leaked', async () => {
    let completed = false;
    service.confirm({ message: 'Confirm?' }).subscribe({ complete: () => { completed = true; } });
    capturedConfig.reject();
    expect(completed).toBe(true);
  });

  // ── options forwarding ────────────────────────────────────────────────────

  it('passes message text through to PrimeNG ConfirmationService', () => {
    service.confirm({ message: 'Are you sure?' });
    expect(capturedConfig.message).toBe('Are you sure?');
  });

  it('uses custom title when provided', () => {
    service.confirm({ message: 'x', title: 'My Title' });
    expect(capturedConfig.header).toBe('My Title');
  });

  it('defaults title to "Confirm" when no title is given', () => {
    service.confirm({ message: 'x' });
    expect(capturedConfig.header).toBe('Confirm');
  });

  it('uses custom confirmLabel when provided', () => {
    service.confirm({ message: 'x', confirmLabel: 'Yes, delete' });
    expect(capturedConfig.acceptLabel).toBe('Yes, delete');
  });

  it('defaults confirmLabel to "Confirm"', () => {
    service.confirm({ message: 'x' });
    expect(capturedConfig.acceptLabel).toBe('Confirm');
  });

  it('sets danger severity when danger=true — destructive actions must look different', () => {
    service.confirm({ message: 'x', danger: true });
    expect(capturedConfig.acceptButtonProps.severity).toBe('danger');
  });

  it('sets primary severity when danger is not set', () => {
    service.confirm({ message: 'x' });
    expect(capturedConfig.acceptButtonProps.severity).toBe('primary');
  });

  it('always sets rejectLabel to "Cancel"', () => {
    service.confirm({ message: 'x' });
    expect(capturedConfig.rejectLabel).toBe('Cancel');
  });
});
