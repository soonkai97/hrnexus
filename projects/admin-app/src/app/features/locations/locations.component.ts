import { Component, OnInit, signal, inject, ViewChild, ElementRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LocationStore, Location, UpsertLocationDto, ToastService, ConfirmService, TrimInputDirective, CanComponentDeactivate, apiErrorMessage } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { Message } from 'primeng/message';

@Component({
  selector: 'app-locations',
  standalone: true,
  imports: [FormsModule, TranslatePipe, TrimInputDirective, Button, InputText, Card, TooltipModule, Message],
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

  @ViewChild('formAnchor', { read: ElementRef }) private formAnchor?: ElementRef<HTMLElement>;

  showForm   = signal(false);
  editing    = signal<Location | null>(null);
  saving     = signal(false);
  error      = signal('');
  deletingId = signal<string | null>(null);

  form: UpsertLocationDto = { name: '' };

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

  private scrollToForm(): void {
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  openAdd(): void    { this.form = { name: '' }; this.editing.set(null); this.touched.set(new Set()); this.showForm.set(true); this.scrollToForm(); }
  openEdit(loc: Location): void { this.form = { name: loc.name }; this.editing.set(loc); this.touched.set(new Set()); this.showForm.set(true); this.scrollToForm(); }
  cancel(): void     { this.showForm.set(false); this.editing.set(null); this.touched.set(new Set()); }

  save(): void {
    const name = this.form.name.trim().toUpperCase();
    if (!name) return;
    this.saving.set(true);
    this.error.set('');
    const dto: UpsertLocationDto = { name };
    const done = () => { this.saving.set(false); this.showForm.set(false); this.editing.set(null); this.toast.success(this.translate.instant('locations.saved')); };
    const fail = (e: unknown) => { const msg = apiErrorMessage(e) ?? this.translate.instant('locations.saveFailed'); this.error.set(msg); this.toast.error(msg); this.saving.set(false); };
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
        error: (e) => { this.deletingId.set(null); const msg = e?.error?.error ?? this.translate.instant('locations.deleteFailed'); this.error.set(msg); this.toast.error(msg); },
      });
    });
  }

  uploadImage(loc: Location, event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.store.uploadImage(loc.id, file).subscribe({
      next: () => this.toast.success(this.translate.instant('locations.imageUploaded')),
      error: (e) => { const msg = e?.error?.error ?? this.translate.instant('locations.uploadFailed'); this.error.set(msg); this.toast.error(msg); },
    });
  }
}
