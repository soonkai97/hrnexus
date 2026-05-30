import { Component, OnInit, signal, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AdminService, TipsPoolDto, TipsDistributionResult, LocationStore, ToastService, todayLocalString, CanComponentDeactivate } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputNumber } from 'primeng/inputnumber';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Select } from 'primeng/select';
import { DatePicker } from 'primeng/datepicker';
import { Skeleton } from 'primeng/skeleton';

@Component({
  selector: 'app-tips',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, TranslatePipe,
    Button, InputNumber, TableModule, Tag, Card, Select, DatePicker, Skeleton],
  templateUrl: './tips.component.html',
  styleUrl: './tips.component.scss',
})
export class TipsComponent implements OnInit, CanComponentDeactivate {
  private readonly adminSvc     = inject(AdminService);
  private readonly toast        = inject(ToastService);
  private readonly translate    = inject(TranslateService);
  readonly locationStore        = inject(LocationStore);

  pools        = signal<TipsPoolDto[]>([]);
  loading      = signal(false);
  showForm     = signal(false);
  distributing = signal(false);
  preview      = signal<TipsDistributionResult | null>(null);
  previewTime  = signal<string | null>(null);

  /** Today's date as "YYYY-MM-DD" in local (venue) time, not UTC. */
  readonly today = todayLocalString();

  get splitMethodOptions() {
    return [
      { label: this.translate.instant('tips.splitEqual'),   value: 'equal' },
      { label: this.translate.instant('tips.splitByHours'), value: 'by_hours' },
    ];
  }

  form = {
    locationId:   '',
    locationName: '',
    shiftDate:    this.today,
    totalAmount:  0,
    splitMethod:  'equal',
  };

  /** Distribute button is enabled only once required fields are filled (mirrors distribute() guard). */
  get isFormValid(): boolean {
    return !!this.form.locationName && !!this.form.shiftDate && this.form.totalAmount > 0;
  }

  ngOnInit(): void {
    this.locationStore.load();
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.adminSvc.getTipsPools().subscribe({
      next:  list => { this.pools.set(list); this.loading.set(false); },
      error: ()   => { this.toast.error(this.translate.instant('tips.loadFailed')); this.loading.set(false); },
    });
  }

  openForm(): void {
    this.form = { locationId: '', locationName: '', shiftDate: this.today, totalAmount: 0, splitMethod: 'equal' };
    this.preview.set(null);
    this.showForm.set(true);
  }

  onLocationChange(): void {
    const loc = this.locationStore.locations().find(l => l.id === this.form.locationId);
    if (loc) this.form.locationName = loc.name;
  }

  distribute(): void {
    if (!this.form.locationName || !this.form.shiftDate || this.form.totalAmount <= 0) {
      this.toast.error(this.translate.instant('tips.fillRequired'));
      return;
    }
    if (this.form.totalAmount > 50_000) {
      this.toast.error(this.translate.instant('tips.capExceeded'));
      return;
    }
    this.distributing.set(true);
    this.adminSvc.distributeTips(this.form).subscribe({
      next: result => {
        this.preview.set(result);
        const now = new Date();
        this.previewTime.set(`${now.toLocaleDateString()} ${now.toLocaleTimeString()}`);
        this.distributing.set(false);
      },
      error: () => {
        this.toast.error(this.translate.instant('tips.distributionFailed'));
        this.distributing.set(false);
      },
    });
  }

  confirm(): void {
    this.showForm.set(false);
    this.preview.set(null);
    this.previewTime.set(null);
    this.load();
  }

  cancel(): void { this.showForm.set(false); this.preview.set(null); this.previewTime.set(null); }

  hasUnsavedChanges(): boolean {
    return this.showForm() && (!!this.form.locationId || this.form.totalAmount > 0);
  }
}
