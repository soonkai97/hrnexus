import { Component, OnInit, signal, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { AdminService, TipsPoolDto, TipsDistributionResult, LocationStore, ToastService, PickerOnlyDirective } from 'shared-lib';

@Component({
  selector: 'app-tips',
  standalone: true,
  imports: [DatePipe, DecimalPipe, FormsModule, TranslatePipe, PickerOnlyDirective],
  templateUrl: './tips.component.html',
  styleUrl: './tips.component.scss',
})
export class TipsComponent implements OnInit {
  private readonly adminSvc     = inject(AdminService);
  private readonly toast        = inject(ToastService);
  readonly locationStore        = inject(LocationStore);

  pools        = signal<TipsPoolDto[]>([]);
  showForm     = signal(false);
  distributing = signal(false);
  preview      = signal<TipsDistributionResult | null>(null);
  previewTime  = signal<string | null>(null);

  /** Today's date as "YYYY-MM-DD" in local (venue) time, not UTC. */
  readonly today = (() => {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  })();

  form = {
    locationId:   '',
    locationName: '',
    shiftDate:    this.today,
    totalAmount:  0,
    splitMethod:  'equal',
  };

  ngOnInit(): void {
    this.locationStore.load();
    this.load();
  }

  load(): void {
    this.adminSvc.getTipsPools().subscribe({
      next:  list => this.pools.set(list),
      error: ()   => this.toast.error('Failed to load tips pools.'),
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
      this.toast.error('Please fill in all required fields.');
      return;
    }
    if (this.form.totalAmount > 50_000) {
      this.toast.error('Total tips amount cannot exceed 50,000. Please verify the amount.');
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
        this.toast.error('Distribution failed.');
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
}
