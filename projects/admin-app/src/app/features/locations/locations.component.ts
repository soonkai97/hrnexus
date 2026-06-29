import { Component, OnInit, signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LocationStore, Location, UpsertLocationDto, ToastService, ConfirmService, TrimInputDirective, CanComponentDeactivate, apiErrorMessage, LocalizedNamePipe } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { Dialog } from 'primeng/dialog';
import { PrimeTemplate } from 'primeng/api';   // provides the pTemplate="footer" directive

@Component({
  selector: 'app-locations',
  standalone: true,
  imports: [FormsModule, TranslatePipe, TrimInputDirective, Button, InputText, Card, TooltipModule, Dialog, PrimeTemplate, LocalizedNamePipe],
  templateUrl: './locations.component.html',
  styleUrl: './locations.component.scss',
})
export class LocationsComponent implements OnInit, CanComponentDeactivate {
  private readonly store      = inject(LocationStore);
  private readonly toast      = inject(ToastService);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly translate  = inject(TranslateService);

  readonly locations = this.store.locations;
  readonly loading   = this.store.loading;

  showForm   = signal(false);
  editing    = signal<Location | null>(null);
  saving     = signal(false);
  deletingId = signal<string | null>(null);

  form: UpsertLocationDto = { name: '', nameZh: '', nameVi: '' };

  readonly touched = signal<Set<string>>(new Set());
  markTouched(field: string): void { this.touched.update(s => new Set(s).add(field)); }
  fieldError(field: 'name'): string {
    if (!this.touched().has(field)) return '';
    return this.form.name?.trim() ? '' : this.translate.instant('locations.nameRequired');
  }

  ngOnInit(): void { this.store.load(); }

  hasUnsavedChanges(): boolean {
    return this.showForm() && !!this.form.name.trim();
  }

  openAdd(): void    { this.form = { name: '', nameZh: '', nameVi: '' }; this.editing.set(null); this.touched.set(new Set()); this.showForm.set(true); }
  openEdit(loc: Location): void { this.form = { name: loc.name, nameZh: loc.nameZh ?? '', nameVi: loc.nameVi ?? '' }; this.editing.set(loc); this.touched.set(new Set()); this.showForm.set(true); }
  cancel(): void     { this.showForm.set(false); this.editing.set(null); this.touched.set(new Set()); }

  save(): void {
    const name = this.form.name.trim().toUpperCase();
    if (!name) return;
    this.saving.set(true);
    const dto: UpsertLocationDto = { name, nameZh: this.form.nameZh?.trim() || null, nameVi: this.form.nameVi?.trim() || null };
    const done = () => { this.saving.set(false); this.showForm.set(false); this.editing.set(null); this.toast.success(this.translate.instant('locations.saved')); };
    const fail = (e: unknown) => { this.toast.error(apiErrorMessage(e) ?? this.translate.instant('locations.saveFailed')); this.saving.set(false); };
    const editingId = this.editing()?.id;
    if (editingId) {
      this.store.update(editingId, dto).subscribe({ next: done, error: fail });
    } else {
      this.store.create(dto).subscribe({ next: done, error: fail });
    }
  }

  remove(id: string): void {
    this.confirmSvc.confirm({
      title: this.translate.instant('locations.confirmDeleteTitle'),
      message: this.translate.instant('locations.confirmDeleteMessage'),
      confirmLabel: this.translate.instant('common.delete'),
      danger: true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.deletingId.set(id);
      this.store.remove(id).subscribe({
        next: () => { this.deletingId.set(null); this.toast.success(this.translate.instant('locations.deleted')); },
        error: (e) => { this.deletingId.set(null); this.toast.error(e?.error?.error ?? this.translate.instant('locations.deleteFailed')); },
      });
    });
  }

  // ── Bulk add (editable row grid — one location name per row) ────────────────
  showBulk   = signal(false);
  bulkRows: { name: string }[] = [];
  bulkSaving = signal(false);

  openBulk(): void { this.bulkRows = [{ name: '' }]; this.showBulk.set(true); }
  addBulkRow(): void { this.bulkRows = [...this.bulkRows, { name: '' }]; }
  removeBulkRow(i: number): void { this.bulkRows = this.bulkRows.filter((_, idx) => idx !== i); }

  get bulkNames(): string[] {
    return this.bulkRows.map(r => r.name.trim()).filter(n => n.length > 0);
  }

  saveBulk(): void {
    const names = this.bulkNames;
    if (!names.length) return;
    this.bulkSaving.set(true);
    this.store.bulkCreate(names).subscribe({
      next: (r) => {
        this.bulkSaving.set(false);
        this.showBulk.set(false);
        this.toast.success(this.translate.instant('locations.bulkAddedToast', { created: r.created, skipped: r.skipped }));
      },
      error: (e) => {
        this.bulkSaving.set(false);
        this.toast.error(apiErrorMessage(e) ?? this.translate.instant('locations.saveFailed'));
      },
    });
  }

  uploadImage(loc: Location, event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.store.uploadImage(loc.id, file).subscribe({
      next: () => this.toast.success(this.translate.instant('locations.imageUploaded')),
      error: (e) => { this.toast.error(e?.error?.error ?? this.translate.instant('locations.uploadFailed')); },
    });
  }
}
