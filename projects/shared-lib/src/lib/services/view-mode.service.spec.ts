import { TestBed } from '@angular/core/testing';
import { ViewModeService } from './view-mode.service';

/**
 * WHY these tests matter: every list screen offers Cards or Table and remembers the choice. The screens must
 * keep working when the browser blocks storage (private windows, cleared site data), and a stale or edited
 * value must never put a screen into a view that does not exist.
 */
describe('ViewModeService', () => {
  let service: ViewModeService;

  const store = new Map<string, string>();
  const fakeStorage = {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, v),
  };

  beforeEach(() => {
    store.clear();
    vi.stubGlobal('localStorage', fakeStorage);
    TestBed.configureTestingModule({ providers: [ViewModeService] });
    service = TestBed.inject(ViewModeService);
  });

  afterEach(() => vi.unstubAllGlobals());

  it('starts every screen on cards', () => {
    expect(service.mode('staff')()).toBe('cards');
  });

  it('remembers the choice per screen and keeps the screens independent', () => {
    service.set('staff', 'table');

    expect(service.mode('staff')()).toBe('table');
    expect(service.mode('roles')()).toBe('cards');
    expect(store.get('view-mode:staff')).toBe('table');
  });

  it('restores a saved choice the next time the app opens', () => {
    store.set('view-mode:roles', 'table');

    const fresh = TestBed.runInInjectionContext(() => new ViewModeService());

    expect(fresh.mode('roles')()).toBe('table');
  });

  it('ignores a stored value that is not a view, so a screen can never be left on a view that does not exist', () => {
    store.set('view-mode:staff', 'carousel');

    const fresh = TestBed.runInInjectionContext(() => new ViewModeService());

    expect(fresh.mode('staff')()).toBe('cards');
  });

  it('returns the same signal for the same screen, so every part of a screen sees one choice', () => {
    expect(service.mode('staff')).toBe(service.mode('staff'));
  });

  it('works when storage is blocked: reads fall back to cards and writes do not throw', () => {
    const blocked = {
      getItem: () => {
        throw new Error('blocked');
      },
      setItem: () => {
        throw new Error('blocked');
      },
    };
    vi.stubGlobal('localStorage', blocked);
    const fresh = TestBed.runInInjectionContext(() => new ViewModeService());

    expect(fresh.mode('staff')()).toBe('cards');
    expect(() => fresh.set('staff', 'table')).not.toThrow();
    expect(fresh.mode('staff')()).toBe('table'); // still works for this visit
  });
});
