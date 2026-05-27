import { Component, OnInit, signal, inject, ViewChild, ElementRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { LocationStore, Location, UpsertLocationDto, ToastService, ConfirmService, TrimInputDirective, CanComponentDeactivate } from 'shared-lib';
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

  readonly locations = this.store.locations;
  readonly loading   = this.store.loading;

  @ViewChild('formAnchor', { read: ElementRef }) private formAnchor?: ElementRef<HTMLElement>;

  showForm   = signal(false);
  editing    = signal<Location | null>(null);
  saving     = signal(false);
  error      = signal('');
  deletingId = signal<string | null>(null);

  form: UpsertLocationDto = { name: '' };

  ngOnInit(): void { this.store.load(); }

  hasUnsavedChanges(): boolean {
    return this.showForm() && !!this.form.name.trim();
  }

  private scrollToForm(): void {
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  openAdd(): void    { this.form = { name: '' }; this.editing.set(null); this.showForm.set(true); this.scrollToForm(); }
  openEdit(loc: Location): void { this.form = { name: loc.name }; this.editing.set(loc); this.showForm.set(true); this.scrollToForm(); }
  cancel(): void     { this.showForm.set(false); this.editing.set(null); }

  save(): void {
    const name = this.form.name.trim().toUpperCase();
    if (!name) return;
    this.saving.set(true);
    this.error.set('');
    const dto: UpsertLocationDto = { name };
    const done = () => { this.saving.set(false); this.showForm.set(false); this.editing.set(null); this.toast.success('Location saved.'); };
    const fail = (e: any) => { const msg = e?.error?.error ?? 'Save failed.'; this.error.set(msg); this.toast.error(msg); this.saving.set(false); };
    const editingId = this.editing()?.id;
    if (editingId) {
      this.store.update(editingId, dto).subscribe({ next: done, error: fail });
    } else {
      this.store.create(dto).subscribe({ next: done, error: fail });
    }
  }

  remove(id: string): void {
    this.confirmSvc.confirm({
      title: 'Delete Location',
      message: 'Are you sure you want to delete this location?',
      confirmLabel: 'Delete',
      danger: true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.deletingId.set(id);
      this.store.remove(id).subscribe({
        next: () => { this.deletingId.set(null); this.toast.success('Location deleted.'); },
        error: (e) => { this.deletingId.set(null); const msg = e?.error?.error ?? 'Delete failed.'; this.error.set(msg); this.toast.error(msg); },
      });
    });
  }

  uploadImage(loc: Location, event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.store.uploadImage(loc.id, file).subscribe({
      next: () => this.toast.success('Image uploaded.'),
      error: (e) => { const msg = e?.error?.error ?? 'Upload failed.'; this.error.set(msg); this.toast.error(msg); },
    });
  }
}
