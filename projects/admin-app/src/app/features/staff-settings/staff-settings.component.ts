import { Component, OnInit, signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import {
  StaffConfigService,
  StaffWorkTypeDto,
  StaffSettlementDto,
  UpsertWorkTypeRequest,
  UpsertSettlementRequest,
  StaffExpenseCategoryDto,
  UpsertExpenseCategoryRequest,
  StaffLoanTypeDto,
  UpsertLoanTypeRequest,
  ToastService,
  ConfirmService,
  TrimInputDirective,
} from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { Select } from 'primeng/select';
import { Checkbox } from 'primeng/checkbox';
import { Tag } from 'primeng/tag';
import { Divider } from 'primeng/divider';
import { Message } from 'primeng/message';
import { Tabs, TabList, Tab, TabPanels, TabPanel } from 'primeng/tabs';

export const LOAN_DIRECTION_OPTIONS: { label: string; value: boolean }[] = [
  { label: 'Advance (Cash Out)',  value: true  },
  { label: 'Repayment (Cash In)', value: false },
];

export const SEVERITY_OPTIONS: { label: string; value: string }[] = [
  { label: 'Success (green)',  value: 'success'   },
  { label: 'Warn (orange)',    value: 'warn'      },
  { label: 'Danger (red)',     value: 'danger'    },
  { label: 'Info (blue)',      value: 'info'      },
  { label: 'Secondary (grey)', value: 'secondary' },
  { label: 'Contrast',         value: 'contrast'  },
];

type SeverityValue = 'success' | 'warn' | 'danger' | 'info' | 'secondary' | 'contrast';

@Component({
  selector: 'app-staff-settings',
  standalone: true,
  imports: [
    FormsModule, TranslatePipe, TrimInputDirective,
    Button, InputText, InputNumber, Select, Checkbox, Tag, Divider, Message,
    Tabs, TabList, Tab, TabPanels, TabPanel,
  ],
  templateUrl: './staff-settings.component.html',
  styleUrl:    './staff-settings.component.scss',
})
export class StaffSettingsComponent implements OnInit {
  private readonly configSvc  = inject(StaffConfigService);
  private readonly toast      = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);

  readonly workTypes         = this.configSvc.workTypes;
  readonly settlements       = this.configSvc.settlements;
  readonly expenseCategories = this.configSvc.expenseCategories;
  readonly loanTypes       = this.configSvc.loanTypes;
  readonly loanDirectionOpts = LOAN_DIRECTION_OPTIONS;
  readonly severityOpts      = SEVERITY_OPTIONS;

  // ── Deleting state ────────────────────────────────────────────────────────
  deletingWtId = signal<string | null>(null);
  deletingEcId = signal<string | null>(null);
  deletingStId = signal<string | null>(null);
  deletingLtId = signal<string | null>(null);

  // ── Work type form ────────────────────────────────────────────────────────
  showWtForm  = signal(false);
  editingWt   = signal<StaffWorkTypeDto | null>(null);
  savingWt    = signal(false);
  wtError     = signal('');
  wtForm: UpsertWorkTypeRequest = this.emptyWtForm();

  // ── Settlement form ───────────────────────────────────────────────────────
  showStForm  = signal(false);
  editingSt   = signal<StaffSettlementDto | null>(null);
  savingSt    = signal(false);
  stError     = signal('');
  stForm: UpsertSettlementRequest = this.emptyStForm();

  // ── Expense category form ─────────────────────────────────────────────────
  showEcForm  = signal(false);
  editingEc   = signal<StaffExpenseCategoryDto | null>(null);
  savingEc    = signal(false);
  ecError     = signal('');
  ecForm: UpsertExpenseCategoryRequest = this.emptyEcForm();

  // ── Loan type form ────────────────────────────────────────────────────────
  showLtForm  = signal(false);
  editingLt   = signal<StaffLoanTypeDto | null>(null);
  savingLt    = signal(false);
  ltError     = signal('');
  ltForm: UpsertLoanTypeRequest = this.emptyLtForm();

  ngOnInit(): void {
    this.configSvc.loadWorkTypes();
    this.configSvc.loadSettlements();
    this.configSvc.loadExpenseCategories();
    this.configSvc.loadLoanTypes();
  }

  // ── Work type actions ─────────────────────────────────────────────────────
  openAddWt(): void {
    this.wtForm = this.emptyWtForm();
    this.editingWt.set(null);
    this.wtError.set('');
    this.showWtForm.set(true);
  }

  openEditWt(item: StaffWorkTypeDto): void {
    this.wtForm = { key: item.key, label: item.label, autoCalc: item.autoCalc, hasPrivate: item.hasPrivate, displayOrder: item.displayOrder, isActive: item.isActive };
    this.editingWt.set(item);
    this.wtError.set('');
    this.showWtForm.set(true);
  }

  cancelWt(): void { this.showWtForm.set(false); this.editingWt.set(null); }

  saveWt(): void {
    if (!this.wtForm.key.trim() || !this.wtForm.label.trim()) return;
    this.savingWt.set(true);
    this.wtError.set('');
    const done = () => { this.savingWt.set(false); this.showWtForm.set(false); this.editingWt.set(null); this.toast.success('Work type saved.'); };
    const fail = (e: any) => { const msg = e?.error?.error ?? 'Save failed.'; this.wtError.set(msg); this.toast.error(msg); this.savingWt.set(false); };
    const editing = this.editingWt();
    if (editing) {
      this.configSvc.updateWorkType(editing.id, this.wtForm).subscribe({ next: done, error: fail });
    } else {
      this.configSvc.createWorkType(this.wtForm).subscribe({ next: done, error: fail });
    }
  }

  removeWt(item: StaffWorkTypeDto): void {
    this.confirmSvc.confirm({
      title: 'Delete Work Type',
      message: `Delete "${item.label}" (${item.key})?`,
      confirmLabel: 'Delete',
      danger: true,
    }).subscribe(ok => {
      if (!ok) return;
      this.deletingWtId.set(item.id);
      this.configSvc.deleteWorkType(item.id).subscribe({
        next:  () => { this.deletingWtId.set(null); this.toast.success('Work type deleted.'); },
        error: (e) => { this.deletingWtId.set(null); this.toast.error(e?.error?.error ?? 'Delete failed.'); },
      });
    });
  }

  // ── Settlement actions ────────────────────────────────────────────────────
  openAddSt(): void {
    this.stForm = this.emptyStForm();
    this.editingSt.set(null);
    this.stError.set('');
    this.showStForm.set(true);
  }

  openEditSt(item: StaffSettlementDto): void {
    this.stForm = { key: item.key, label: item.label, severity: item.severity, displayOrder: item.displayOrder, isActive: item.isActive };
    this.editingSt.set(item);
    this.stError.set('');
    this.showStForm.set(true);
  }

  cancelSt(): void { this.showStForm.set(false); this.editingSt.set(null); }

  saveSt(): void {
    if (!this.stForm.key.trim() || !this.stForm.label.trim()) return;
    this.savingSt.set(true);
    this.stError.set('');
    const done = () => { this.savingSt.set(false); this.showStForm.set(false); this.editingSt.set(null); this.toast.success('Settlement saved.'); };
    const fail = (e: any) => { const msg = e?.error?.error ?? 'Save failed.'; this.stError.set(msg); this.toast.error(msg); this.savingSt.set(false); };
    const editing = this.editingSt();
    if (editing) {
      this.configSvc.updateSettlement(editing.id, this.stForm).subscribe({ next: done, error: fail });
    } else {
      this.configSvc.createSettlement(this.stForm).subscribe({ next: done, error: fail });
    }
  }

  removeSt(item: StaffSettlementDto): void {
    this.confirmSvc.confirm({
      title: 'Delete Settlement',
      message: `Delete "${item.label}" (${item.key})?`,
      confirmLabel: 'Delete',
      danger: true,
    }).subscribe(ok => {
      if (!ok) return;
      this.deletingStId.set(item.id);
      this.configSvc.deleteSettlement(item.id).subscribe({
        next:  () => { this.deletingStId.set(null); this.toast.success('Settlement deleted.'); },
        error: (e) => { this.deletingStId.set(null); this.toast.error(e?.error?.error ?? 'Delete failed.'); },
      });
    });
  }

  // ── Expense category actions ──────────────────────────────────────────────
  openAddEc(): void {
    this.ecForm = this.emptyEcForm();
    this.editingEc.set(null);
    this.ecError.set('');
    this.showEcForm.set(true);
  }

  openEditEc(item: StaffExpenseCategoryDto): void {
    this.ecForm = { key: item.key, label: item.label, displayOrder: item.displayOrder, isActive: item.isActive };
    this.editingEc.set(item);
    this.ecError.set('');
    this.showEcForm.set(true);
  }

  cancelEc(): void { this.showEcForm.set(false); this.editingEc.set(null); }

  saveEc(): void {
    if (!this.ecForm.key.trim() || !this.ecForm.label.trim()) return;
    this.savingEc.set(true);
    this.ecError.set('');
    const done = () => { this.savingEc.set(false); this.showEcForm.set(false); this.editingEc.set(null); this.toast.success('Category saved.'); };
    const fail = (e: any) => { const msg = e?.error?.error ?? 'Save failed.'; this.ecError.set(msg); this.toast.error(msg); this.savingEc.set(false); };
    const editing = this.editingEc();
    if (editing) {
      this.configSvc.updateExpenseCategory(editing.id, this.ecForm).subscribe({ next: done, error: fail });
    } else {
      this.configSvc.createExpenseCategory(this.ecForm).subscribe({ next: done, error: fail });
    }
  }

  removeEc(item: StaffExpenseCategoryDto): void {
    this.confirmSvc.confirm({
      title: 'Delete Category',
      message: `Delete "${item.label}" (${item.key})?`,
      confirmLabel: 'Delete',
      danger: true,
    }).subscribe(ok => {
      if (!ok) return;
      this.deletingEcId.set(item.id);
      this.configSvc.deleteExpenseCategory(item.id).subscribe({
        next:  () => { this.deletingEcId.set(null); this.toast.success('Category deleted.'); },
        error: (e) => { this.deletingEcId.set(null); this.toast.error(e?.error?.error ?? 'Delete failed.'); },
      });
    });
  }

  // ── Loan type actions ─────────────────────────────────────────────────────
  openAddLt(): void {
    this.ltForm = this.emptyLtForm();
    this.editingLt.set(null);
    this.ltError.set('');
    this.showLtForm.set(true);
  }

  openEditLt(item: StaffLoanTypeDto): void {
    this.ltForm = { key: item.key, label: item.label, isAdvance: item.isAdvance, displayOrder: item.displayOrder, isActive: item.isActive };
    this.editingLt.set(item);
    this.ltError.set('');
    this.showLtForm.set(true);
  }

  cancelLt(): void { this.showLtForm.set(false); this.editingLt.set(null); }

  saveLt(): void {
    if (!this.ltForm.key.trim() || !this.ltForm.label.trim()) return;
    this.savingLt.set(true);
    this.ltError.set('');
    const done = () => { this.savingLt.set(false); this.showLtForm.set(false); this.editingLt.set(null); this.toast.success('Loan type saved.'); };
    const fail = (e: any) => { const msg = e?.error?.error ?? 'Save failed.'; this.ltError.set(msg); this.toast.error(msg); this.savingLt.set(false); };
    const editing = this.editingLt();
    if (editing) {
      this.configSvc.updateLoanType(editing.id, this.ltForm).subscribe({ next: done, error: fail });
    } else {
      this.configSvc.createLoanType(this.ltForm).subscribe({ next: done, error: fail });
    }
  }

  removeLt(item: StaffLoanTypeDto): void {
    if (item.isSystem) return;
    this.confirmSvc.confirm({
      title: 'Delete Loan Type',
      message: `Delete "${item.label}" (${item.key})?`,
      confirmLabel: 'Delete',
      danger: true,
    }).subscribe(ok => {
      if (!ok) return;
      this.deletingLtId.set(item.id);
      this.configSvc.deleteLoanType(item.id).subscribe({
        next:  () => { this.deletingLtId.set(null); this.toast.success('Loan type deleted.'); },
        error: (e) => { this.deletingLtId.set(null); this.toast.error(e?.error?.error ?? 'Delete failed.'); },
      });
    });
  }

  private emptyLtForm(): UpsertLoanTypeRequest {
    return { key: '', label: '', isAdvance: true, displayOrder: 0, isActive: true };
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  severityType(s: string): SeverityValue {
    const known: SeverityValue[] = ['success','warn','danger','info','secondary','contrast'];
    return known.includes(s as SeverityValue) ? (s as SeverityValue) : 'secondary';
  }

  private emptyWtForm(): UpsertWorkTypeRequest {
    return { key: '', label: '', autoCalc: false, hasPrivate: false, displayOrder: 0, isActive: true };
  }

  private emptyStForm(): UpsertSettlementRequest {
    return { key: '', label: '', severity: 'secondary', displayOrder: 0, isActive: true };
  }

  private emptyEcForm(): UpsertExpenseCategoryRequest {
    return { key: '', label: '', displayOrder: 0, isActive: true };
  }
}
