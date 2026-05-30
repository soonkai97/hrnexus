import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import {
  StaffStore,
  StaffService,
  StaffConfigService,
  StaffEntryDto,
  UpsertEntryRequest,
  StaffExpenseDto,
  UpsertExpenseRequest,
  StaffLoanDto,
  UpsertLoanRequest,
  ToastService,
  ConfirmService,
  toLocalDateString,
  TrimInputDirective,
  CanComponentDeactivate,
  apiErrorMessage,
} from 'shared-lib';
import { Button } from 'primeng/button';
import { Select } from 'primeng/select';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { Skeleton } from 'primeng/skeleton';
import { Message } from 'primeng/message';
import { Divider } from 'primeng/divider';
import { Tabs, TabList, Tab } from 'primeng/tabs';

@Component({
  selector: 'app-staff-ledger',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, TrimInputDirective, Button, Select, InputText, InputNumber, Card, Tag, Skeleton, Message, Divider, Tabs, TabList, Tab],
  templateUrl: './staff-ledger.component.html',
  styleUrl: './staff-ledger.component.scss',
})
export class StaffLedgerComponent implements OnInit, CanComponentDeactivate {
  private readonly staffStore  = inject(StaffStore);
  private readonly staffSvc    = inject(StaffService);
  private readonly configSvc   = inject(StaffConfigService);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);
  private readonly translate   = inject(TranslateService);

  // ── Reference data ──────────────────────────────────────────────────────────
  readonly allStaff    = this.staffStore.staff;
  readonly staffOpts   = computed(() => this.allStaff().map(s => ({ label: `${s.fullName} (${s.staffId})`, value: s.id })));
  get MONTH_OPTS(): { label: string; value: number }[] {
    return Array.from({ length: 12 }, (_, i) => ({ label: this.monthName(i + 1), value: i + 1 }));
  }
  /** Localised month name for month number 1–12. */
  monthName(month: number): string {
    return this.translate.instant(`staffLedger.month${month}`);
  }
  readonly yearOpts    = Array.from({ length: 8 }, (_, i) => new Date().getFullYear() - i);
  readonly workTypes         = computed(() => this.configSvc.workTypes().filter(t => t.isActive).map(t => ({ label: t.label, value: t.key })));
  readonly settlements       = computed(() => this.configSvc.settlements().filter(s => s.isActive).map(s => ({ label: s.label, value: s.key })));
  readonly expenseCategories = computed(() => this.configSvc.expenseCategories().filter(c => c.isActive).map(c => ({ label: c.label, value: c.key })));

  // ── Filter state ─────────────────────────────────────────────────────────────
  selectedStaffId = signal<string | null>(null);
  selectedYear    = signal(new Date().getFullYear());
  selectedMonth   = signal(new Date().getMonth() + 1);   // 1–12

  // ── Entry list state ─────────────────────────────────────────────────────────
  entries  = signal<StaffEntryDto[]>([]);
  loading  = signal(false);

  // ── Form state ───────────────────────────────────────────────────────────────
  showForm  = signal(false);
  editingId = signal<string | null>(null);
  saving    = signal(false);
  formError = signal('');

  form: UpsertEntryRequest = this.emptyForm();

  // ── Tab state ─────────────────────────────────────────────────────────────────
  activeTab = signal<'entries' | 'expenses' | 'loans'>('entries');

  // ── Expense state ─────────────────────────────────────────────────────────────
  expenses         = signal<StaffExpenseDto[]>([]);
  expenseLoading   = signal(false);
  showExpenseForm  = signal(false);
  savingExpense    = signal(false);
  expenseFormError = signal('');
  expenseForm: UpsertExpenseRequest = this.emptyExpenseForm();
  editingExpenseId = signal<string | null>(null);

  // ── Loan state ────────────────────────────────────────────────────────────────
  loans         = signal<StaffLoanDto[]>([]);
  loanLoading   = signal(false);
  showLoanForm  = signal(false);
  savingLoan    = signal(false);
  loanFormError = signal('');
  loanForm: UpsertLoanRequest = this.emptyLoanForm();

  // ── Constants ─────────────────────────────────────────────────────────────────
  readonly loanTypeOptions = computed(() =>
    this.configSvc.loanTypes()
      .filter(lt => lt.isActive)
      .map(lt => ({ label: lt.label, value: lt.key }))
  );

  // ── Computed summary ─────────────────────────────────────────────────────────
  totalVenue      = computed(() => this.entries().reduce((s, e) => s + e.venueEarnings, 0));
  totalPrivateNet = computed(() => this.entries().reduce((s, e) => s + e.privateNet, 0));
  entryCount      = computed(() => this.entries().length);

  totalExpenses  = computed(() => this.expenses().reduce((s, e) => s + e.amount, 0));
  netAdvances   = computed(() => {
    const keys = new Set(this.configSvc.loanTypes().filter(lt => lt.isAdvance).map(lt => lt.key));
    return this.loans().filter(l => keys.has(l.loanType)).reduce((s, l) => s + l.amount, 0);
  });
  netRepayments = computed(() => {
    const keys = new Set(this.configSvc.loanTypes().filter(lt => !lt.isAdvance).map(lt => lt.key));
    return this.loans().filter(l => keys.has(l.loanType)).reduce((s, l) => s + l.amount, 0);
  });
  netLoanBalance = computed(() => this.netAdvances() - this.netRepayments());
  netPayout      = computed(() => this.totalVenue() + this.totalPrivateNet() - this.totalExpenses());

  // ── Lifecycle ────────────────────────────────────────────────────────────────
  ngOnInit(): void {
    this.staffStore.loadStaff();
    this.configSvc.loadWorkTypes();
    this.configSvc.loadSettlements();
    this.configSvc.loadExpenseCategories();
    this.configSvc.loadLoanTypes();
  }

  // ── Filter actions ────────────────────────────────────────────────────────────
  onStaffChange(id: string | null): void {
    this.selectedStaffId.set(id);
    this.entries.set([]);
    this.expenses.set([]);
    this.loans.set([]);
    this.showForm.set(false);
    if (id) {
      this.loadEntries();
      this.loadExpenses();
      this.loadLoans();
    }
  }

  onYearChange(year: number): void {
    this.selectedYear.set(year);
    if (this.selectedStaffId()) { this.loadEntries(); this.loadExpenses(); }
  }

  onMonthChange(month: number): void {
    this.selectedMonth.set(month);
    if (this.selectedStaffId()) { this.loadEntries(); this.loadExpenses(); }
  }

  private loadEntries(): void {
    const staffId = this.selectedStaffId();
    if (!staffId) return;
    this.loading.set(true);
    this.staffSvc.listEntries(staffId, this.selectedYear(), this.selectedMonth()).subscribe({
      next:  (data) => { this.entries.set(data); this.loading.set(false); },
      error: ()     => { this.toast.error(this.translate.instant('staffLedger.loadEntriesFailed')); this.loading.set(false); },
    });
  }

  private loadExpenses(): void {
    const staffId = this.selectedStaffId();
    if (!staffId) return;
    this.expenseLoading.set(true);
    this.staffSvc.listExpenses(staffId, this.selectedYear(), this.selectedMonth()).subscribe({
      next:  (data) => { this.expenses.set(data); this.expenseLoading.set(false); },
      error: ()     => { this.toast.error(this.translate.instant('staffLedger.loadExpensesFailed')); this.expenseLoading.set(false); },
    });
  }

  private loadLoans(): void {
    const staffId = this.selectedStaffId();
    if (!staffId) return;
    this.loanLoading.set(true);
    this.staffSvc.listLoans(staffId).subscribe({
      next:  (data) => { this.loans.set(data); this.loanLoading.set(false); },
      error: ()     => { this.toast.error(this.translate.instant('staffLedger.loadLoansFailed')); this.loanLoading.set(false); },
    });
  }

  // ── Form actions ──────────────────────────────────────────────────────────────
  openAdd(): void {
    this.form = this.emptyForm();
    this.editingId.set(null);
    this.formError.set('');
    this.showForm.set(true);
  }

  openEdit(entry: StaffEntryDto): void {
    this.form = {
      staffId:       entry.staffId,
      entryDate:     entry.entryDate,
      workType:      entry.workType,
      venueUnits:    entry.venueUnits,
      venueRate:     entry.venueRate,
      venueEarnings: entry.venueEarnings,
      privateGross:  entry.privateGross,
      privateNet:    entry.privateNet,
      settlement:    entry.settlement,
      notes:         entry.notes,
    };
    this.editingId.set(entry.id);
    this.formError.set('');
    this.showForm.set(true);
  }

  cancel(): void {
    this.showForm.set(false);
    this.editingId.set(null);
    this.formError.set('');
  }

  save(): void {
    const staffId = this.selectedStaffId();
    if (!staffId || !this.form.entryDate || !this.form.workType) return;

    this.saving.set(true);
    this.formError.set('');

    const req: UpsertEntryRequest = { ...this.form, staffId };

    const onDone = () => {
      this.saving.set(false);
      this.showForm.set(false);
      this.editingId.set(null);
      this.toast.success(this.translate.instant('staffLedger.savedSuccess'));
      this.loadEntries();
    };
    const onFail = (e: unknown) => {
      const msg = apiErrorMessage(e) ?? this.translate.instant('staffLedger.saveFailed');
      this.formError.set(msg);
      this.toast.error(msg);
      this.saving.set(false);
    };

    const id = this.editingId();
    if (id) {
      this.staffSvc.updateEntry(id, req).subscribe({ next: onDone, error: onFail });
    } else {
      this.staffSvc.createEntry(req).subscribe({ next: onDone, error: onFail });
    }
  }

  remove(entry: StaffEntryDto): void {
    this.confirmSvc.confirm({
      message: this.translate.instant('staffLedger.confirmDelete'),
      danger:  true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.staffSvc.deleteEntry(entry.id).subscribe({
        next:  () => { this.toast.success(this.translate.instant('staffLedger.deletedSuccess')); this.loadEntries(); },
        error: ()  => this.toast.error(this.translate.instant('staffLedger.deleteFailed')),
      });
    });
  }

  // ── Expense actions ───────────────────────────────────────────────────────────
  openAddExpense(): void {
    this.expenseForm = this.emptyExpenseForm();
    this.editingExpenseId.set(null);
    this.expenseFormError.set('');
    this.showExpenseForm.set(true);
  }

  openEditExpense(e: StaffExpenseDto): void {
    this.expenseForm = {
      staffId:  e.staffId,
      year:     e.year,
      month:    e.month,
      category: e.category,
      amount:   e.amount,
      notes:    e.notes,
    };
    this.editingExpenseId.set(e.id);
    this.expenseFormError.set('');
    this.showExpenseForm.set(true);
  }

  cancelExpense(): void {
    this.showExpenseForm.set(false);
    this.editingExpenseId.set(null);
    this.expenseFormError.set('');
  }

  saveExpense(): void {
    const staffId = this.selectedStaffId();
    if (!staffId) return;

    this.savingExpense.set(true);
    this.expenseFormError.set('');

    const req: UpsertExpenseRequest = { ...this.expenseForm, staffId };

    const onDone = () => {
      this.savingExpense.set(false);
      this.showExpenseForm.set(false);
      this.editingExpenseId.set(null);
      this.toast.success(this.translate.instant('staffLedger.expenseSaved'));
      this.loadExpenses();
    };
    const onFail = (e: unknown) => {
      const msg = apiErrorMessage(e) ?? this.translate.instant('staffLedger.expenseFailed');
      this.expenseFormError.set(msg);
      this.toast.error(msg);
      this.savingExpense.set(false);
    };

    const id = this.editingExpenseId();
    if (id) {
      this.staffSvc.updateExpense(id, req).subscribe({ next: onDone, error: onFail });
    } else {
      this.staffSvc.createExpense(req).subscribe({ next: onDone, error: onFail });
    }
  }

  removeExpense(e: StaffExpenseDto): void {
    this.confirmSvc.confirm({
      message: this.translate.instant('staffLedger.confirmDeleteExpense'),
      danger:  true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.staffSvc.deleteExpense(e.id).subscribe({
        next:  () => { this.toast.success(this.translate.instant('staffLedger.expenseDeleted')); this.loadExpenses(); },
        error: ()  => this.toast.error(this.translate.instant('staffLedger.expenseDeleteFailed')),
      });
    });
  }

  // ── Loan actions ──────────────────────────────────────────────────────────────
  openAddLoan(): void {
    this.loanForm = this.emptyLoanForm();
    this.loanFormError.set('');
    this.showLoanForm.set(true);
  }

  cancelLoan(): void {
    this.showLoanForm.set(false);
    this.loanFormError.set('');
  }

  saveLoan(): void {
    const staffId = this.selectedStaffId();
    if (!staffId) return;

    this.savingLoan.set(true);
    this.loanFormError.set('');

    const req: UpsertLoanRequest = { ...this.loanForm, staffId };

    const onDone = () => {
      this.savingLoan.set(false);
      this.showLoanForm.set(false);
      this.toast.success(this.translate.instant('staffLedger.loanSaved'));
      this.loadLoans();
    };
    const onFail = (e: unknown) => {
      const msg = apiErrorMessage(e) ?? this.translate.instant('staffLedger.loanFailed');
      this.loanFormError.set(msg);
      this.toast.error(msg);
      this.savingLoan.set(false);
    };

    this.staffSvc.createLoan(req).subscribe({ next: onDone, error: onFail });
  }

  removeLoan(l: StaffLoanDto): void {
    this.confirmSvc.confirm({
      message: this.translate.instant('staffLedger.confirmDeleteLoan'),
      danger:  true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.staffSvc.deleteLoan(l.id).subscribe({
        next:  () => { this.toast.success(this.translate.instant('staffLedger.loanDeleted')); this.loadLoans(); },
        error: ()  => this.toast.error(this.translate.instant('staffLedger.loanDeleteFailed')),
      });
    });
  }

  // ── Auto-calc helpers ────────────────────────────────────────────────────────
  /** Called whenever units or rate changes — auto-calc if the work type has autoCalc=true. */
  recalcVenueEarnings(): void {
    const wt = this.configSvc.workTypes().find(t => t.key === this.form.workType);
    if (wt?.autoCalc) {
      this.form.venueEarnings = +(this.form.venueUnits * this.form.venueRate).toFixed(2);
    }
  }

  /** Returns true for work types where private money fields are relevant. */
  hasPrivateFields(): boolean {
    return this.configSvc.workTypes().find(t => t.key === this.form.workType)?.hasPrivate ?? false;
  }

  // ── Print statement ───────────────────────────────────────────────────────────
  selectedStaffName = computed(() => {
    const id = this.selectedStaffId();
    if (!id) return '';
    const s = this.allStaff().find(st => st.id === id);
    return s ? `${s.fullName} (${s.staffId})` : '';
  });

  exportStatementCsv(): void {
    const staffId = this.selectedStaffId();
    if (!staffId) return;
    const staff      = this.allStaff().find(s => s.id === staffId);
    const staffName  = this.selectedStaffName();
    const year       = this.selectedYear();
    const month      = this.selectedMonth();
    const monthLabel = `${this.monthName(month)} ${year}`;
    const t = (key: string) => this.translate.instant(key);
    const q = (v: string | number) => `"${String(v).replace(/"/g, '""')}"`;

    const rows: string[] = [
      t('staffLedger.csvStatementTitle'),
      `${t('staffLedger.csvStaff')},${q(staffName)}`,
      `${t('staffLedger.csvPeriod')},${q(monthLabel)}`,
      ``,
      t('staffLedger.csvShiftEntries'),
      [
        t('staffLedger.csvColDate'),
        t('staffLedger.csvColWorkType'),
        t('staffLedger.csvColUnits'),
        t('staffLedger.csvColRate'),
        t('staffLedger.csvColVenue'),
        t('staffLedger.csvColPrivateNet'),
        t('staffLedger.csvColSettlement'),
        t('staffLedger.csvColNotes'),
      ].map(q).join(','),
      ...this.entries().map(e => [
        q(e.entryDate),
        q(this.workTypeLabel(e.workType)),
        e.venueUnits,
        e.venueRate,
        e.venueEarnings,
        e.privateNet,
        q(this.settlementLabel(e.settlement)),
        q(e.notes ?? ''),
      ].join(',')),
      `${q(t('staffLedger.csvSubtotal'))},"","","",${this.totalVenue()},${this.totalPrivateNet()},"",""`,
      ``,
      t('staffLedger.csvDeductions'),
      [
        t('staffLedger.csvColCategory'),
        t('staffLedger.csvColAmount'),
        t('staffLedger.csvColNotes'),
      ].map(q).join(','),
      ...this.expenses().map(e => [
        q(this.expenseCategoryLabel(e.category)),
        e.amount,
        q(e.notes ?? ''),
      ].join(',')),
      `${q(t('staffLedger.csvTotalDeductions'))},${this.totalExpenses()},""`,
      ``,
      t('staffLedger.csvSummary'),
      `${q(t('staffLedger.csvNetPayout'))},${this.netPayout()}`,
      ``,
      t('staffLedger.csvLoansAdvances'),
      [
        t('staffLedger.csvColDate'),
        t('staffLedger.csvColType'),
        t('staffLedger.csvColAmount'),
        t('staffLedger.csvColPurpose'),
      ].map(q).join(','),
      ...this.loans().map(l => [
        q(l.loanDate),
        q(this.loanTypeLabel(l.loanType)),
        l.amount,
        q(l.purpose ?? ''),
      ].join(',')),
      `${q(t('staffLedger.csvNetLoanBalance'))},${this.netLoanBalance()},"",""`,
    ];

    const csv  = rows.join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = `statement_${staff?.staffId ?? staffId}_${year}_${String(month).padStart(2,'0')}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  // ── Display helpers ───────────────────────────────────────────────────────────
  settlementSeverity(key: string): 'success' | 'warn' | 'danger' | 'info' | 'secondary' | 'contrast' {
    const severity = this.configSvc.settlements().find(s => s.key === key)?.severity ?? 'secondary';
    const known = ['success','warn','danger','info','secondary','contrast'] as const;
    return known.includes(severity as any) ? severity as any : 'secondary';
  }

  workTypeLabel(key: string): string {
    return this.configSvc.workTypes().find(t => t.key === key)?.label ?? key;
  }

  settlementLabel(key: string): string {
    return this.configSvc.settlements().find(s => s.key === key)?.label ?? key;
  }

  expenseCategoryLabel(key: string): string {
    return this.configSvc.expenseCategories().find(c => c.key === key)?.label ?? key;
  }

  loanTypeLabel(key: string): string {
    return this.configSvc.loanTypes().find(lt => lt.key === key)?.label ?? key;
  }

  private emptyForm(): UpsertEntryRequest {
    const year  = this.selectedYear();
    const month = this.selectedMonth();
    const lastDay = new Date(year, month, 0).getDate();
    const defDate = `${year}-${String(month).padStart(2,'0')}-${String(lastDay).padStart(2,'0')}`;
    return {
      staffId:       this.selectedStaffId() ?? '',
      entryDate:     defDate,
      workType:      'VENUECLOCK',
      venueUnits:    0,
      venueRate:     0,
      venueEarnings: 0,
      privateGross:  0,
      privateNet:    0,
      settlement:    'PENDING',
      notes:         null,
    };
  }

  private emptyExpenseForm(): UpsertExpenseRequest {
    return {
      staffId:  this.selectedStaffId() ?? '',
      year:     this.selectedYear(),
      month:    this.selectedMonth(),
      category: this.configSvc.expenseCategories().find(c => c.isActive)?.key ?? 'MEAL',
      amount:   0,
      notes:    null,
    };
  }

  private emptyLoanForm(): UpsertLoanRequest {
    return {
      staffId:  this.selectedStaffId() ?? '',
      loanDate: toLocalDateString(new Date()),
      loanType: this.configSvc.loanTypes().find(lt => lt.isActive)?.key ?? 'ADVANCE',
      amount:   0,
      purpose:  null,
    };
  }

  hasUnsavedChanges(): boolean {
    return this.showForm() || this.showExpenseForm() || this.showLoanForm();
  }
}
