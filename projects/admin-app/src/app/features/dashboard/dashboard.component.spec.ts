import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';
import { DashboardComponent } from './dashboard.component';
import { SignalRService, AdminService, ToastService } from 'shared-lib';

/**
 * DashboardComponent — admin landing page.
 *
 * WHY these tests matter:
 *  - The dashboard is the redirect target for every guarded/unknown route, so a failed
 *    load must end the loading skeleton and tell the manager, not hang or crash.
 *  - The live tag reflects the notification hub connection; it must follow the SignalR signal.
 */

const fakeDashboard = { stats: { totalStaff: 10 } };

describe('DashboardComponent', () => {
  let mockSignalR:  { isConnected: ReturnType<typeof vi.fn> };
  let mockAdminSvc: { getDashboard: ReturnType<typeof vi.fn> };
  let mockToastSvc: { error: ReturnType<typeof vi.fn>; success: ReturnType<typeof vi.fn> };

  function createComponent(connected = false) {
    mockSignalR.isConnected.mockReturnValue(connected);
    return TestBed.createComponent(DashboardComponent).componentInstance;
  }

  beforeEach(async () => {
    mockSignalR  = { isConnected: vi.fn().mockReturnValue(false) };
    mockAdminSvc = { getDashboard: vi.fn().mockReturnValue(of(fakeDashboard)) };
    mockToastSvc = { error: vi.fn(), success: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideTranslateService({ defaultLanguage: 'en' }),
        { provide: SignalRService, useValue: mockSignalR },
        { provide: AdminService,   useValue: mockAdminSvc },
        { provide: ToastService,   useValue: mockToastSvc },
        MessageService,
        ConfirmationService,
      ],
    }).compileComponents();
  });

  describe('ngOnInit()', () => {
    it('starts in loading state before data arrives', () => {
      expect(createComponent().loading()).toBe(true);
    });

    it('loads the dashboard stats and ends loading', () => {
      const c = createComponent();
      c.ngOnInit();
      expect(mockAdminSvc.getDashboard).toHaveBeenCalledTimes(1);
      expect(c.stats()).toEqual(fakeDashboard.stats);
      expect(c.loading()).toBe(false);
    });

    it('ends loading and shows an error toast when the dashboard fails to load', () => {
      mockAdminSvc.getDashboard.mockReturnValue(throwError(() => new Error('boom')));
      const c = createComponent();
      c.ngOnInit();
      expect(c.loading()).toBe(false);
      expect(c.stats()).toBeNull();
      expect(mockToastSvc.error).toHaveBeenCalledTimes(1);
    });
  });

  describe('hubConnected', () => {
    it('is true when the SignalR hub is connected', () => {
      expect(createComponent(true).hubConnected()).toBe(true);
    });

    it('is false when the SignalR hub is not connected', () => {
      expect(createComponent(false).hubConnected()).toBe(false);
    });
  });
});
