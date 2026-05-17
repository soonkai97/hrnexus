import { TestBed } from '@angular/core/testing';
import { MessageService } from 'primeng/api';
import { ToastService } from './toast.service';

/**
 * ToastService — wraps PrimeNG MessageService with domain-specific shortcuts.
 *
 * WHY these tests matter:
 *  - PrimeNG severity is 'warn' not 'warning' — a mismatch silently renders the
 *    wrong toast colour. This mapping must be verified explicitly.
 *  - Error toasts use a longer duration (5000 ms) so users have time to read them;
 *    using the default 4000 ms would cut critical messages short.
 *  - Every caller depends on `detail` (not `summary`) for the message text, so
 *    the field mapping must be consistent.
 */
describe('ToastService', () => {
  let service: ToastService;
  let msgSvc: MessageService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [MessageService],
    });
    service = TestBed.inject(ToastService);
    msgSvc  = TestBed.inject(MessageService);
    vi.spyOn(msgSvc, 'add');
  });

  afterEach(() => vi.restoreAllMocks());

  // ── success() ─────────────────────────────────────────────────────────────

  it('success() calls MessageService.add with severity "success"', () => {
    service.success('Staff saved.');
    expect(msgSvc.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'success', detail: 'Staff saved.' })
    );
  });

  it('success() uses the default 4000 ms duration', () => {
    service.success('OK');
    expect(msgSvc.add).toHaveBeenCalledWith(
      expect.objectContaining({ life: 4000 })
    );
  });

  // ── error() ────────────────────────────────────────────────────────────────

  it('error() calls MessageService.add with severity "error"', () => {
    service.error('Save failed.');
    expect(msgSvc.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'error', detail: 'Save failed.' })
    );
  });

  it('error() uses 5000 ms duration so users have time to read the message', () => {
    service.error('Something went wrong.');
    expect(msgSvc.add).toHaveBeenCalledWith(
      expect.objectContaining({ life: 5000 })
    );
  });

  // ── warning() ──────────────────────────────────────────────────────────────

  it('warning() maps to PrimeNG severity "warn" (not "warning") — avoids silent colour mismatch', () => {
    service.warning('Period not closed.');
    expect(msgSvc.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'warn', detail: 'Period not closed.' })
    );
  });

  it('warning() uses the default 4000 ms duration', () => {
    service.warning('Check this.');
    expect(msgSvc.add).toHaveBeenCalledWith(
      expect.objectContaining({ life: 4000 })
    );
  });

  // ── show() — direct access ─────────────────────────────────────────────────

  it('show() with type "info" calls MessageService.add with severity "info"', () => {
    service.show('Loading…', 'info');
    expect(msgSvc.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'info', detail: 'Loading…', life: 4000 })
    );
  });

  it('show() accepts a custom duration', () => {
    service.show('Custom', 'success', 8000);
    expect(msgSvc.add).toHaveBeenCalledWith(
      expect.objectContaining({ life: 8000 })
    );
  });

  it('show() defaults to type "info" when no type is given', () => {
    service.show('Hello');
    expect(msgSvc.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'info' })
    );
  });
});
