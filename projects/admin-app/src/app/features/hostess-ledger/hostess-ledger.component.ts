import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import {
  StaffStore,
  HostessService,
  HostessEntryDto,
  UpsertEntryRequest,
  WorkType,
  Settlement,
  WORK_TYPE_LABELS,
  SETTLEMENT_LABELS,
  ToastService,
  ConfirmService,
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

interface MonthOption { label: string; year: number; month: number; }

const MONTHS = ['January','February','March','April','May','June','July','August','September','October','November','December'];

function buildMonthOptions(): MonthOption[] {
  const now   = new Date();
  const opts: MonthOption[] = [];
  for (let i = 0; i < 12; i++) {
    const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
    opts.push({ label: `${MONTHS[d.getMonth()]} ${d.getFullYear()}`, year: d.getFullYear(), month: d.getMonth() + 1 });
  }
  return opts;
}

export const WORK_TYPES: WorkType[] = ['VENUECLOCK','TABLERUN','OVERTIME','OVERNIGHT','PRIVATEHOURS','SELFDEDUCT','REST','SLOWNIGHT'];
export const SETTLEMENTS: Settlement[] = ['PENDING','SETTLED','CASH','BANKIN','NOTCOLLECTED','COMP'];

@Component({
  selector: 'app-hostess-ledger',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, Button, Select, InputText, InputNumber, Card, Tag, Skeleton, Message, Divider],
  templateUrl: './hostess-ledger.component.html',
  styleUrl: './hostess-ledger.component.scss',
})
export class HostessLedgerComponent implements OnInit {
  private readonly staffStore  = inject(StaffStore);
  private readonly hostessSvc  = inject(HostessService);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);
  private readonly translate   = inject(TranslateService);

  // ── Reference data ──────────────────────────────────────────────────────────
  readonly allStaff   = this.staffStore.staff;
  readonly staffOpts  = computed(() => this.allStaff().map(s => ({ label: `${s.fullName} (${s.staffId})`, value: s.id })));
  readonly monthOpts  = buildMonthOptions();
  readonly workTypes  = WORK_TYPES.map(t => ({ label: WORK_TYPE_LABELS[t], value: t }));
  readonly settlements = SETTLEMENTS.map(s => ({ label: SETTLEMENT_LABELS[s], value: s }));

  WORK_TYPE_LABELS = WORK_TYPE_LABELS;
  SETTLEMENT_LABELS = SETTLEMENT_LABELS;

  // ── Filter state ─────────────────────────────────────────────────────────────
  selectedStaffId = signal<string | null>(null);
  selectedMonth   = signal<MonthOption>(this.monthOpts[0]);

  // ── Entry list state ─────────────────────────────────────────────────────────
  entries  = signal<HostessEntryDto[]>([]);
  loading  = signal(false);

  // ── Form state ───────────────────────────────────────────────────────────────
  showForm  = signal(false);
  editingId = signal<string | null>(null);
  saving    = signal(false);
  formError = signal('');

  form: UpsertEntryRequest = this.emptyForm();

  // ── Computed summary ─────────────────────────────────────────────────────────
  totalVenue      = computed(() => this.entries().reduce((s, e) => s + e.venueEarnings, 0));
  totalPrivateNet = computed(() => this.entries().reduce((s, e) => s + e.privateNet, 0));
  entryCount      = computed(() => this.entries().length);

  // ── Lifecycle ────────────────────────────────────────────────────────────────
  ngOnInit(): void {
    this.staffStore.loadStaff();
  }

  // ── Filter actions ────────────────────────────────────────────────────────────
  onStaffChange(id: string | null): void {
    this.selectedStaffId.set(id);
    this.entries.set([]);
    this.showForm.set(false);
    if (id) this.loadEntries();
  }

  onMonthChange(opt: MonthOption): void {
    this.selectedMonth.set(opt);
    if (this.selectedStaffId()) this.loadEntries();
  }

  private loadEntries(): void {
    const staffId = this.selectedStaffId();
    if (!staffId) return;
    const { year, month } = this.selectedMonth();
    this.loading.set(true);
    this.hostessSvc.listEntries(staffId, year, month).subscribe({
      next:     (data) => { this.entries.set(data); this.loading.set(false); },
      error:    ()     => { this.toast.error('Failed to load entries.'); this.loading.set(false); },
    });
  }

  // ── Form actions ──────────────────────────────────────────────────────────────
  openAdd(): void {
    this.form = this.emptyForm();
    this.editingId.set(null);
    this.formError.set('');
    this.showForm.set(true);
  }

  openEdit(entry: HostessEntryDto): void {
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
      this.toast.success(this.translate.instant('hostessLedger.savedSuccess'));
      this.loadEntries();
    };
    const onFail = (e: any) => {
      const msg = e?.error?.error ?? this.translate.instant('hostessLedger.saveFailed');
      this.formError.set(msg);
      this.toast.error(msg);
      this.saving.set(false);
    };

    const id = this.editingId();
    if (id) {
      this.hostessSvc.updateEntry(id, req).subscribe({ next: onDone, error: onFail });
    } else {
      this.hostessSvc.createEntry(req).subscribe({ next: onDone, error: onFail });
    }
  }

  remove(entry: HostessEntryDto): void {
    this.confirmSvc.confirm({
      message: this.translate.instant('hostessLedger.confirmDelete'),
      danger:  true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.hostessSvc.deleteEntry(entry.id).subscribe({
        next:  () => { this.toast.success(this.translate.instant('hostessLedger.deletedSuccess')); this.loadEntries(); },
        error: ()  => this.toast.error(this.translate.instant('hostessLedger.deleteFailed')),
      });
    });
  }

  // ── Auto-calc helpers ────────────────────────────────────────────────────────
  /** Called whenever units or rate changes on a clock/table work type. */
  recalcVenueEarnings(): void {
    if (['VENUECLOCK','TABLERUN','PRIVATEHOURS'].includes(this.form.workType)) {
      this.form.venueEarnings = +(this.form.venueUnits * this.form.venueRate).toFixed(2);
    }
  }

  /** Returns true for work types where private money fields are relevant. */
  hasPrivateFields(): boolean {
    return ['OVERNIGHT','PRIVATEHOURS'].includes(this.form.workType);
  }

  // ── Display helpers ───────────────────────────────────────────────────────────
  settlementSeverity(s: Settlement): 'success' | 'warn' | 'danger' | 'info' | 'secondary' | 'contrast' {
    switch (s) {
      case 'SETTLED':      return 'success';
      case 'BANKIN':       return 'info';
      case 'PENDING':      return 'warn';
      case 'NOTCOLLECTED': return 'danger';
      case 'COMP':         return 'secondary';
      default:             return 'secondary';
    }
  }

  private emptyForm(): UpsertEntryRequest {
    const { year, month } = this.selectedMonth();
    const lastDay  = new Date(year, month, 0).getDate();
    const defDate  = `${year}-${String(month).padStart(2,'0')}-${String(lastDay).padStart(2,'0')}`;
    return {
      staffId: this.selectedStaffId() ?? '',
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
}
