import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { AdminService } from './admin.service';

/**
 * AdminService — HTTP transport for the admin dashboard.
 * WHY: the dashboard's only data source; a wrong URL silently blanks the landing page.
 */
describe('AdminService', () => {
  let service: AdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AdminService, provideHttpClient(withXhr()), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('getDashboard()', () => {
    it('calls GET /api/admin/dashboard and passes the response through', () => {
      let result: unknown;
      service.getDashboard().subscribe(r => (result = r));
      const req = http.expectOne('/api/admin/dashboard');
      expect(req.request.method).toBe('GET');
      req.flush({ stats: { totalStaff: 7 } });
      expect(result).toEqual({ stats: { totalStaff: 7 } });
    });
  });
});
