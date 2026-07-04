import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { VersionService } from './version.service';

describe('VersionService', () => {
  let service: VersionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [VersionService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(VersionService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts with the dev fallback before load()', () => {
    expect(service.info().version).toBe('dev');
  });

  it('loads the stamped build version from /version.json', () => {
    service.load();
    const req = http.expectOne(r => r.url === '/version.json');
    req.flush({ version: '20260704.01', commit: 'a1b2c3d', builtAt: '2026-07-04T02:00Z' });
    expect(service.info().version).toBe('20260704.01');
    expect(service.info().commit).toBe('a1b2c3d');
  });

  it('cache-busts the request so a redeploy is never served stale', () => {
    service.load();
    const req = http.expectOne(r => r.url === '/version.json');
    expect(req.request.params.has('t')).toBe(true);   // timestamp query param defeats the SW cache
    req.flush({ version: '20260704.02' });
  });

  it('only fetches once even if load() is called repeatedly', () => {
    service.load();
    service.load();
    service.load();
    const reqs = http.match(r => r.url === '/version.json');
    expect(reqs.length).toBe(1);
    reqs[0].flush({ version: '20260704.01' });
  });

  it('keeps the dev fallback when the file is missing (local/offline)', () => {
    service.load();
    http.expectOne(r => r.url === '/version.json').flush('Not Found', { status: 404, statusText: 'Not Found' });
    expect(service.info().version).toBe('dev');
  });
});
