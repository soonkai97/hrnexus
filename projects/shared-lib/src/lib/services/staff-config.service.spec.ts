import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { StaffConfigService } from './staff-config.service';

/**
 * StaffConfigService — signal-based config store for work types, settlements,
 * expense categories, and loan types with HTTP persistence.
 * WHY: validates that:
 *  - load*() methods populate the correct signal from the right URL
 *  - mutation methods (create/update/delete) POST/PUT/DELETE to the right URL
 *  - every mutation automatically triggers a reload GET (the tap() side-effect)
 *    so the signal stays fresh without a manual refresh call
 */
describe('StaffConfigService', () => {
  let service: StaffConfigService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [StaffConfigService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(StaffConfigService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── Work Types ────────────────────────────────────────────────────────────

  describe('loadWorkTypes()', () => {
    it('calls GET /api/staff-config/work-types', () => {
      service.loadWorkTypes();
      const req = http.expectOne('/api/staff-config/work-types');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('sets the workTypes signal from the API response', () => {
      const data = [{ id: 'wt-1', name: '写钟', isActive: true }];
      service.loadWorkTypes();
      http.expectOne('/api/staff-config/work-types').flush(data);
      expect(service.workTypes()).toEqual(data);
    });
  });

  describe('createWorkType()', () => {
    it('calls POST /api/staff-config/work-types with the request body', () => {
      const body: any = { name: '跑台', isActive: true };
      service.createWorkType(body).subscribe();
      const req = http.expectOne('/api/staff-config/work-types');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ id: 'wt-new' });
      // flush the automatic reload triggered by tap()
      http.expectOne('/api/staff-config/work-types').flush([]);
    });

    it('triggers a reload GET after creation so the signal stays fresh', () => {
      service.createWorkType({ name: '包夜' } as any).subscribe();
      http.expectOne(r => r.method === 'POST' && r.url === '/api/staff-config/work-types').flush({ id: 'wt-2' });
      // The tap() inside createWorkType calls loadWorkTypes() — a second GET must follow
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/work-types');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });

  describe('updateWorkType()', () => {
    it('calls PUT /api/staff-config/work-types/{id} with the request body', () => {
      const body: any = { name: '写钟 (updated)', isActive: false };
      service.updateWorkType('wt-1', body).subscribe();
      const req = http.expectOne('/api/staff-config/work-types/wt-1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush(null);
      http.expectOne('/api/staff-config/work-types').flush([]);
    });

    it('triggers a reload GET after update', () => {
      service.updateWorkType('wt-1', {} as any).subscribe();
      http.expectOne(r => r.method === 'PUT').flush(null);
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/work-types');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });

  describe('deleteWorkType()', () => {
    it('calls DELETE /api/staff-config/work-types/{id}', () => {
      service.deleteWorkType('wt-3').subscribe();
      const req = http.expectOne('/api/staff-config/work-types/wt-3');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
      http.expectOne('/api/staff-config/work-types').flush([]);
    });

    it('triggers a reload GET after deletion', () => {
      service.deleteWorkType('wt-3').subscribe();
      http.expectOne(r => r.method === 'DELETE').flush(null);
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/work-types');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });

  // ── Settlements ───────────────────────────────────────────────────────────

  describe('loadSettlements()', () => {
    it('calls GET /api/staff-config/settlements', () => {
      service.loadSettlements();
      const req = http.expectOne('/api/staff-config/settlements');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('sets the settlements signal from the API response', () => {
      const data = [{ id: 's-1', name: 'Cash', isActive: true }];
      service.loadSettlements();
      http.expectOne('/api/staff-config/settlements').flush(data);
      expect(service.settlements()).toEqual(data);
    });
  });

  describe('createSettlement()', () => {
    it('calls POST /api/staff-config/settlements with the request body', () => {
      const body: any = { name: 'Bank Transfer', isActive: true };
      service.createSettlement(body).subscribe();
      const req = http.expectOne('/api/staff-config/settlements');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ id: 's-new' });
      http.expectOne('/api/staff-config/settlements').flush([]);
    });

    it('triggers a reload GET after creation', () => {
      service.createSettlement({ name: 'Cash' } as any).subscribe();
      http.expectOne(r => r.method === 'POST' && r.url === '/api/staff-config/settlements').flush({ id: 's-2' });
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/settlements');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });

  describe('updateSettlement()', () => {
    it('calls PUT /api/staff-config/settlements/{id} with the request body', () => {
      const body: any = { name: 'Cash (updated)' };
      service.updateSettlement('s-1', body).subscribe();
      const req = http.expectOne('/api/staff-config/settlements/s-1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush(null);
      http.expectOne('/api/staff-config/settlements').flush([]);
    });
  });

  describe('deleteSettlement()', () => {
    it('calls DELETE /api/staff-config/settlements/{id}', () => {
      service.deleteSettlement('s-5').subscribe();
      const req = http.expectOne('/api/staff-config/settlements/s-5');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
      http.expectOne('/api/staff-config/settlements').flush([]);
    });

    it('triggers a reload GET after deletion', () => {
      service.deleteSettlement('s-5').subscribe();
      http.expectOne(r => r.method === 'DELETE').flush(null);
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/settlements');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });

  // ── Expense Categories ────────────────────────────────────────────────────

  describe('loadExpenseCategories()', () => {
    it('calls GET /api/staff-config/expense-categories', () => {
      service.loadExpenseCategories();
      const req = http.expectOne('/api/staff-config/expense-categories');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('sets the expenseCategories signal from the API response', () => {
      const data = [{ id: 'ec-1', name: 'Transport', isActive: true }];
      service.loadExpenseCategories();
      http.expectOne('/api/staff-config/expense-categories').flush(data);
      expect(service.expenseCategories()).toEqual(data);
    });
  });

  describe('createExpenseCategory()', () => {
    it('calls POST /api/staff-config/expense-categories with the request body', () => {
      const body: any = { name: 'Meals', isActive: true };
      service.createExpenseCategory(body).subscribe();
      const req = http.expectOne('/api/staff-config/expense-categories');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ id: 'ec-new' });
      http.expectOne('/api/staff-config/expense-categories').flush([]);
    });

    it('triggers a reload GET after creation', () => {
      service.createExpenseCategory({ name: 'Meals' } as any).subscribe();
      http.expectOne(r => r.method === 'POST' && r.url === '/api/staff-config/expense-categories').flush({ id: 'ec-2' });
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/expense-categories');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });

  describe('updateExpenseCategory()', () => {
    it('calls PUT /api/staff-config/expense-categories/{id} with the request body', () => {
      const body: any = { name: 'Meals (updated)' };
      service.updateExpenseCategory('ec-1', body).subscribe();
      const req = http.expectOne('/api/staff-config/expense-categories/ec-1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush(null);
      http.expectOne('/api/staff-config/expense-categories').flush([]);
    });

    it('triggers a reload GET after update', () => {
      service.updateExpenseCategory('ec-1', {} as any).subscribe();
      http.expectOne(r => r.method === 'PUT').flush(null);
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/expense-categories');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });

  describe('deleteExpenseCategory()', () => {
    it('calls DELETE /api/staff-config/expense-categories/{id}', () => {
      service.deleteExpenseCategory('ec-9').subscribe();
      const req = http.expectOne('/api/staff-config/expense-categories/ec-9');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
      http.expectOne('/api/staff-config/expense-categories').flush([]);
    });
  });

  // ── Loan Types ────────────────────────────────────────────────────────────

  describe('loadLoanTypes()', () => {
    it('calls GET /api/staff-config/loan-types', () => {
      service.loadLoanTypes();
      const req = http.expectOne('/api/staff-config/loan-types');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('sets the loanTypes signal from the API response', () => {
      const data = [{ id: 'lt-1', name: 'Advance', isActive: true }];
      service.loadLoanTypes();
      http.expectOne('/api/staff-config/loan-types').flush(data);
      expect(service.loanTypes()).toEqual(data);
    });
  });

  describe('createLoanType()', () => {
    it('calls POST /api/staff-config/loan-types with the request body', () => {
      const body: any = { name: 'Emergency', isActive: true };
      service.createLoanType(body).subscribe();
      const req = http.expectOne('/api/staff-config/loan-types');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ id: 'lt-new' });
      http.expectOne('/api/staff-config/loan-types').flush([]);
    });

    it('triggers a reload GET after creation', () => {
      service.createLoanType({ name: 'Emergency' } as any).subscribe();
      http.expectOne(r => r.method === 'POST' && r.url === '/api/staff-config/loan-types').flush({ id: 'lt-2' });
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/loan-types');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });

  describe('updateLoanType()', () => {
    it('calls PUT /api/staff-config/loan-types/{id} with the request body', () => {
      const body: any = { name: 'Advance (updated)' };
      service.updateLoanType('lt-1', body).subscribe();
      const req = http.expectOne('/api/staff-config/loan-types/lt-1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush(null);
      http.expectOne('/api/staff-config/loan-types').flush([]);
    });

    it('triggers a reload GET after update', () => {
      service.updateLoanType('lt-1', {} as any).subscribe();
      http.expectOne(r => r.method === 'PUT').flush(null);
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/loan-types');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });

  describe('deleteLoanType()', () => {
    it('calls DELETE /api/staff-config/loan-types/{id}', () => {
      service.deleteLoanType('lt-7').subscribe();
      const req = http.expectOne('/api/staff-config/loan-types/lt-7');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
      http.expectOne('/api/staff-config/loan-types').flush([]);
    });

    it('uses the provided id in the URL — not hardcoded', () => {
      service.deleteLoanType('lt-abc').subscribe();
      const req = http.expectOne(r => r.method === 'DELETE');
      expect(req.request.url).toContain('lt-abc');
      req.flush(null);
      http.expectOne('/api/staff-config/loan-types').flush([]);
    });

    it('triggers a reload GET after deletion', () => {
      service.deleteLoanType('lt-7').subscribe();
      http.expectOne(r => r.method === 'DELETE').flush(null);
      const reload = http.expectOne(r => r.method === 'GET' && r.url === '/api/staff-config/loan-types');
      expect(reload).toBeTruthy();
      reload.flush([]);
    });
  });
});
