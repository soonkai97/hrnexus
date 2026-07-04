import { TestBed } from '@angular/core/testing';
import { VersionService } from './version.service';

describe('VersionService', () => {
  function setMeta(content: string | null): void {
    document.querySelectorAll('meta[name="build-version"]').forEach(m => m.remove());
    if (content !== null) {
      const m = document.createElement('meta');
      m.setAttribute('name', 'build-version');
      m.setAttribute('content', content);
      document.head.appendChild(m);
    }
  }

  // VersionService reads the meta at construction, so seed the DOM BEFORE injecting.
  function makeService(): VersionService {
    TestBed.configureTestingModule({ providers: [VersionService] });
    return TestBed.inject(VersionService);
  }

  afterEach(() => setMeta(null));

  it('reads the stamped build version from the index.html meta tag', () => {
    setMeta('20260704.05');
    expect(makeService().info().version).toBe('20260704.05');
  });

  it('falls back to "dev" when the meta tag is absent (local ng serve)', () => {
    setMeta(null);
    expect(makeService().info().version).toBe('dev');
  });

  it('falls back to "dev" when the __BUILD_VERSION__ placeholder was never replaced', () => {
    setMeta('__BUILD_VERSION__');
    expect(makeService().info().version).toBe('dev');
  });

  it('trims surrounding whitespace from the stamped value', () => {
    setMeta('  20260704.06  ');
    expect(makeService().info().version).toBe('20260704.06');
  });

  it('load() is a no-op and does not throw (version already read synchronously)', () => {
    setMeta('20260704.05');
    const svc = makeService();
    expect(() => svc.load()).not.toThrow();
    expect(svc.info().version).toBe('20260704.05');
  });
});
