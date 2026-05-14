import { Component, OnInit, signal, computed, inject, ViewChild, ElementRef } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { AnnouncementService, AdminAnnouncementDto, ConfirmService, ToastService, TooltipDirective, PickerOnlyDirective } from 'shared-lib';

@Component({
  selector: 'app-announcements',
  standalone: true,
  imports: [DatePipe, FormsModule, TranslatePipe, TooltipDirective, PickerOnlyDirective],
  templateUrl: './announcements.component.html',
  styleUrl: './announcements.component.scss',
})
export class AnnouncementsComponent implements OnInit {
  private readonly announcementSvc = inject(AnnouncementService);
  private readonly confirmSvc      = inject(ConfirmService);
  private readonly toast           = inject(ToastService);

  @ViewChild('formAnchor') private formAnchor?: ElementRef<HTMLElement>;

  items    = signal<AdminAnnouncementDto[]>([]);
  showForm = signal(false);
  editing  = signal<string | null>(null);
  saving   = signal(false);
  loading  = signal(false);

  readonly pageSize = 10;
  currentPage  = signal(1);
  totalPages   = computed(() => Math.max(1, Math.ceil(this.items().length / this.pageSize)));
  pagedItems   = computed(() => {
    const page = Math.min(this.currentPage(), this.totalPages());
    return this.items().slice((page - 1) * this.pageSize, page * this.pageSize);
  });

  // ── Action menu ────────────────────────────────────────────────────────────
  openMenuId  = signal<string | null>(null);
  menuPos     = signal<{ top: number; right: number } | null>(null);
  menuTarget  = computed(() => this.items().find(i => i.id === this.openMenuId()) ?? null);

  toggleMenu(id: string, event: MouseEvent): void {
    if (this.openMenuId() === id) { this.closeMenu(); return; }
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const right = window.innerWidth - rect.right;
    this.menuPos.set({ top: rect.bottom + 4, right });
    this.openMenuId.set(id);
  }

  closeMenu(): void { this.openMenuId.set(null); this.menuPos.set(null); }

  form = { title: '', body: '', targetRoles: '*', expiresAt: '' };

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.announcementSvc.getAll().subscribe({
      next:  list => { this.items.set(list); this.loading.set(false); },
      error: ()   => { this.toast.error('Failed to load announcements.'); this.loading.set(false); },
    });
  }

  private scrollToForm(): void {
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  openAdd(): void {
    this.form = { title: '', body: '', targetRoles: '*', expiresAt: '' };
    this.editing.set(null);
    this.showForm.set(true);
    this.scrollToForm();
  }

  startEdit(item: AdminAnnouncementDto): void {
    this.form = {
      title:       item.title,
      body:        item.body,
      targetRoles: item.targetRoles,
      expiresAt:   item.expiresAt ? item.expiresAt.slice(0, 10) : '',
    };
    this.editing.set(item.id);
    this.showForm.set(true);
    this.scrollToForm();
  }

  save(): void {
    if (!this.form.title.trim() || !this.form.body.trim()) {
      this.toast.error('Title and body are required.');
      return;
    }
    if (this.form.title.trim().length > 200) {
      this.toast.error('Title cannot exceed 200 characters.');
      return;
    }
    if (this.form.body.trim().length > 4000) {
      this.toast.error('Body cannot exceed 4000 characters.');
      return;
    }
    if (this.form.expiresAt) {
      const expiry = new Date(this.form.expiresAt);
      if (expiry <= new Date()) {
        this.toast.error('Expiry date must be in the future.');
        return;
      }
    }
    this.saving.set(true);
    const dto = {
      title:       this.form.title.trim(),
      body:        this.form.body.trim(),
      targetRoles: this.form.targetRoles.trim() || '*',
      expiresAt:   this.form.expiresAt || null,
    };
    const editId = this.editing();
    const onSuccess = () => {
      this.toast.success(editId ? 'Announcement updated.' : 'Announcement created.');
      this.saving.set(false);
      this.showForm.set(false);
      this.load();
    };
    const onError = () => {
      this.toast.error('Save failed.');
      this.saving.set(false);
    };

    if (editId) {
      this.announcementSvc.update(editId, dto).subscribe({ next: onSuccess, error: onError });
    } else {
      this.announcementSvc.create(dto).subscribe({ next: onSuccess, error: onError });
    }
  }

  remove(id: string): void {
    this.confirmSvc.confirm({ message: 'Delete this announcement?', confirmLabel: 'Delete', danger: true })
      .subscribe(ok => {
        if (!ok) return;
        this.announcementSvc.delete(id).subscribe({
          next:  () => { this.toast.success('Deleted.'); this.load(); },
          error: () => this.toast.error('Delete failed.'),
        });
      });
  }

  cancel(): void { this.showForm.set(false); }
}
