import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { StaffService } from './staff.service';

/**
 * StaffService — HTTP transport for the staff ledger (entries, expenses, loans, summary).
 * WHY: ensures each method hits the correct URL with the right params/body so that
 * ledger data is always read from and written to the intended backend endpoints.
 */
describe('StaffService', () => {
  let service: StaffService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [StaffService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(StaffService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── listEntries() ─────────────────────────────────────────────────────────

  describe('listEntries()', () => {
    it('calls GET /api/staff-entries with staffId, year, and month params', () => {
      service.listEntries('emp-1', 2026, 5).subscribe();
      const req = http.expectOne(r => r.url === '/api/staff-entries');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('staffId=emp-1');
      expect(req.request.urlWithParams).toContain('year=2026');
      expect(req.request.urlWithParams).toContain('month=5');
      req.flush([]);
    });

    it('uses the provided staffId in the params — not hardcoded', () => {
      service.listEntries('emp-xyz', 2026, 1).subscribe();
      const req = http.expectOne(r => r.url === '/api/staff-entries');
      expect(req.request.urlWithParams).toContain('staffId=emp-xyz');
      req.flush([]);
    });
  });

  // ── createEntry() ─────────────────────────────────────────────────────────

  describe('createEntry()', () => {
    it('calls POST /api/staff-entries with the request body', () => {
      const body: any = { staffId: 'emp-1', workTypeId: 'wt-1', amount: 500, date: '2026-05-01', notes: '' };
      service.createEntry(body).subscribe();
      const req = http.expectOne('/api/staff-entries');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ id: 'entry-new' });
    });

    it('returns {id} from the API response', () => {
      let result: { id: string } | undefined;
      service.createEntry({} as any).subscribe(v => (result = v));
      http.expectOne('/api/staff-entries').flush({ id: 'created-id' });
      expect(result).toEqual({ id: 'created-id' });
    });
  });

  // ── updateEntry() ─────────────────────────────────────────────────────────

  describe('updateEntry()', () => {
    it('calls PUT /api/staff-entries/{id} with the request body', () => {
      const body: any = { amount: 600, notes: 'updated' };
      service.updateEntry('entry-5', body).subscribe();
      const req = http.expectOne('/api/staff-entries/entry-5');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush(null);
    });

    it('uses the provided id in the URL — not hardcoded', () => {
      service.updateEntry('entry-abc', {} as any).subscribe();
      const req = http.expectOne('/api/staff-entries/entry-abc');
      expect(req.request.url).toContain('entry-abc');
      req.flush(null);
    });
  });

  // ── deleteEntry() ─────────────────────────────────────────────────────────

  describe('deleteEntry()', () => {
    it('calls DELETE /api/staff-entries/{id}', () => {
      service.deleteEntry('entry-99').subscribe();
      const req = http.expectOne('/api/staff-entries/entry-99');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  // ── listExpenses() ────────────────────────────────────────────────────────

  describe('listExpenses()', () => {
    it('calls GET /api/staff-entries/expenses with staffId, year, and month params', () => {
      service.listExpenses('emp-2', 2026, 3).subscribe();
      const req = http.expectOne(r => r.url === '/api/staff-entries/expenses');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('staffId=emp-2');
      expect(req.request.urlWithParams).toContain('year=2026');
      expect(req.request.urlWithParams).toContain('month=3');
      req.flush([]);
    });
  });

  // ── createExpense() ───────────────────────────────────────────────────────

  describe('createExpense()', () => {
    it('calls POST /api/staff-entries/expenses with the request body', () => {
      const body: any = { staffId: 'emp-2', categoryId: 'cat-1', amount: 50, date: '2026-03-10' };
      service.createExpense(body).subscribe();
      const req = http.expectOne('/api/staff-entries/expenses');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ id: 'exp-new' });
    });

    it('returns {id} from the API response', () => {
      let result: { id: string } | undefined;
      service.createExpense({} as any).subscribe(v => (result = v));
      http.expectOne('/api/staff-entries/expenses').flush({ id: 'exp-id' });
      expect(result).toEqual({ id: 'exp-id' });
    });
  });

  // ── updateExpense() ───────────────────────────────────────────────────────

  describe('updateExpense()', () => {
    it('calls PUT /api/staff-entries/expenses/{id} with the request body', () => {
      const body: any = { amount: 75 };
      service.updateExpense('exp-3', body).subscribe();
      const req = http.expectOne('/api/staff-entries/expenses/exp-3');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush(null);
    });
  });

  // ── deleteExpense() ───────────────────────────────────────────────────────

  describe('deleteExpense()', () => {
    it('calls DELETE /api/staff-entries/expenses/{id}', () => {
      service.deleteExpense('exp-7').subscribe();
      const req = http.expectOne('/api/staff-entries/expenses/exp-7');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  // ── listLoans() ───────────────────────────────────────────────────────────

  describe('listLoans()', () => {
    it('calls GET /api/staff-entries/loans with staffId param', () => {
      service.listLoans('emp-3').subscribe();
      const req = http.expectOne(r => r.url === '/api/staff-entries/loans');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('staffId=emp-3');
      req.flush([]);
    });

    it('uses the provided staffId — not hardcoded', () => {
      service.listLoans('emp-xyz').subscribe();
      const req = http.expectOne(r => r.url === '/api/staff-entries/loans');
      expect(req.request.urlWithParams).toContain('staffId=emp-xyz');
      req.flush([]);
    });
  });

  // ── createLoan() ──────────────────────────────────────────────────────────

  describe('createLoan()', () => {
    it('calls POST /api/staff-entries/loans with the request body', () => {
      const body: any = { staffId: 'emp-3', loanTypeId: 'lt-1', amount: 1000, date: '2026-05-01' };
      service.createLoan(body).subscribe();
      const req = http.expectOne('/api/staff-entries/loans');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ id: 'loan-new' });
    });

    it('returns {id} from the API response', () => {
      let result: { id: string } | undefined;
      service.createLoan({} as any).subscribe(v => (result = v));
      http.expectOne('/api/staff-entries/loans').flush({ id: 'loan-id' });
      expect(result).toEqual({ id: 'loan-id' });
    });
  });

  // ── deleteLoan() ──────────────────────────────────────────────────────────

  describe('deleteLoan()', () => {
    it('calls DELETE /api/staff-entries/loans/{id}', () => {
      service.deleteLoan('loan-4').subscribe();
      const req = http.expectOne('/api/staff-entries/loans/loan-4');
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });

    it('uses the provided id in the URL — not hardcoded', () => {
      service.deleteLoan('loan-abc').subscribe();
      const req = http.expectOne('/api/staff-entries/loans/loan-abc');
      expect(req.request.url).toContain('loan-abc');
      req.flush(null);
    });
  });

  // ── summary() ─────────────────────────────────────────────────────────────

  describe('summary()', () => {
    it('calls GET /api/staff-entries/summary with year and month params', () => {
      service.summary(2026, 5).subscribe();
      const req = http.expectOne(r => r.url === '/api/staff-entries/summary');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toContain('year=2026');
      expect(req.request.urlWithParams).toContain('month=5');
      req.flush([]);
    });

    it('passes through the API response without transformation', () => {
      const data = [{ staffId: 'emp-1', totalPay: 3000 }];
      let result: any;
      service.summary(2026, 5).subscribe(v => (result = v));
      http.expectOne(r => r.url === '/api/staff-entries/summary').flush(data);
      expect(result).toEqual(data);
    });
  });

  // ── outstanding() ─────────────────────────────────────────────────────────

  describe('outstanding()', () => {
    it('calls GET /api/staff-entries/outstanding', () => {
      service.outstanding().subscribe();
      const req = http.expectOne('/api/staff-entries/outstanding');
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('passes through the API response without transformation', () => {
      const data = [{ staffId: 'emp-1', outstanding: 500 }];
      let result: any;
      service.outstanding().subscribe(v => (result = v));
      http.expectOne('/api/staff-entries/outstanding').flush(data);
      expect(result).toEqual(data);
    });
  });

  // ── bulkSettle() ──────────────────────────────────────────────────────────

  describe('bulkSettle()', () => {
    it('calls PUT /api/staff-entries/bulk-settle with entryIds and settlement body', () => {
      const entryIds = ['e1', 'e2', 'e3'];
      service.bulkSettle(entryIds, 'cash').subscribe();
      const req = http.expectOne('/api/staff-entries/bulk-settle');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ entryIds, settlement: 'cash' });
      req.flush({ updated: 3 });
    });

    it('returns {updated} count from the API response', () => {
      let result: { updated: number } | undefined;
      service.bulkSettle(['e1'], 'transfer').subscribe(v => (result = v));
      http.expectOne('/api/staff-entries/bulk-settle').flush({ updated: 1 });
      expect(result).toEqual({ updated: 1 });
    });

    it('uses the provided settlement method in the body — not hardcoded', () => {
      service.bulkSettle(['e1', 'e2'], 'transfer').subscribe();
      const req = http.expectOne('/api/staff-entries/bulk-settle');
      expect(req.request.body.settlement).toBe('transfer');
      req.flush({ updated: 2 });
    });
  });
});
