import { Component, OnInit, signal, computed, inject, ViewChild, ElementRef } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnnouncementService, AdminAnnouncementDto, ConfirmService, ToastService } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Paginator } from 'primeng/paginator';
import { Textarea } from 'primeng/textarea';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressSpinner } from 'primeng/progressspinner';
import { DatePicker } from 'primeng/datepicker';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';

@Component({
  selector: 'app-announcements',
  standalone: true,
  imports: [DatePipe, FormsModule, TranslatePipe,
    Button, InputText, Textarea, TableModule, Tag, Card, TooltipModule, ProgressSpinner, DatePicker, Paginator, Menu],
  templateUrl: './announcements.component.html',
  styleUrl: './announcements.component.scss',
})
export class AnnouncementsComponent implements OnInit {
  private readonly announcementSvc = inject(AnnouncementService);
  private readonly confirmSvc      = inject(ConfirmService);
  private readonly toast           = inject(ToastService);
  private readonly translate       = inject(TranslateService);

  @ViewChild('formAnchor', { read: ElementRef }) private formAnchor?: ElementRef<HTMLElement>;
  @ViewChild('rowMenu') rowMenu!: Menu;

  items      = signal<AdminAnnouncementDto[]>([]);
  showForm   = signal(false);
  editing    = signal<string | null>(null);
  saving     = signal(false);
  loading    = signal(false);
  totalCount = signal(0);
  menuItems  = signal<MenuItem[]>([]);

  readonly pageSize = 10;
  currentPage  = signal(1);
  totalPages   = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize)));
  pagedItems   = this.items.asReadonly();

  loadPage(page: number): void {
    this.currentPage.set(page);
    this.load(page);
  }

  openMenu(item: AdminAnnouncementDto, event: Event): void {
    this.menuItems.set([
      { label: this.translate.instant('common.edit'),   icon: 'pi pi-pencil', command: () => this.startEdit(item) },
      { separator: true },
      { label: this.translate.instant('common.delete'), icon: 'pi pi-trash',  styleClass: 'nc-danger-item', command: () => this.remove(item.id) },
    ]);
    this.rowMenu.toggle(event);
  }

  form = { title: '', body: '', targetRoles: '*', expiresAt: '' };

  ngOnInit(): void { this.load(1); }

  load(page = this.currentPage()): void {
    this.loading.set(true);
    this.announcementSvc.getAll(page, this.pageSize).subscribe({
      next:  result => { this.items.set(result.items); this.totalCount.set(result.total); this.loading.set(false); },
      error: ()     => { this.toast.error('Failed to load announcements.'); this.loading.set(false); },
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
    if (this.form.body.trim().length > 5000) {
      this.toast.error('Body cannot exceed 5000 characters.');
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
    this.confirmSvc.confirm({ title: 'Delete Announcement', message: 'Are you sure you want to delete this announcement?', confirmLabel: 'Delete', danger: true })
      .subscribe(ok => {
        if (!ok) return;
        this.announcementSvc.delete(id).subscribe({
          next:  () => { this.toast.success('Deleted.'); this.load(); },
          error: () => this.toast.error('Delete failed.'),
        });
      });
  }

  get isFormValid(): boolean {
    return !!this.form.title.trim() && !!this.form.body.trim();
  }

  cancel(): void { this.showForm.set(false); }
}
