import { Component, OnInit, signal, computed, inject, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LocationStore, Location, UpsertLocationDto, ToastService, ConfirmService, TrimInputDirective, CanComponentDeactivate, apiErrorMessage, LocalizedNamePipe } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { Dialog } from 'primeng/dialog';
import { Paginator } from 'primeng/paginator';
import { PrimeTemplate } from 'primeng/api';   // provides the pTemplate="footer" directive

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-locations',
  standalone: true,
  imports: [FormsModule, TranslatePipe, TrimInputDirective, Button, InputText, Card, TooltipModule, Dialog, Paginator, PrimeTemplate, LocalizedNamePipe],
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

  // Client-side pagination of the location cards.
  readonly pageRows = 12;
  readonly first    = signal(0);
  readonly pagedLocations = computed(() => {
    const all = this.locations();
    // Clamp when the list shrinks (e.g. after a delete) so we never land on an empty page.
    const start = this.first() >= all.length ? 0 : this.first();
    return all.slice(start, start + this.pageRows);
  });

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

  uploadImage(loc: Location, event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.store.uploadImage(loc.id, file).subscribe({
      next: () => this.toast.success(this.translate.instant('locations.imageUploaded')),
      error: (e) => { this.toast.error(e?.error?.error ?? this.translate.instant('locations.uploadFailed')); },
    });
  }
}
