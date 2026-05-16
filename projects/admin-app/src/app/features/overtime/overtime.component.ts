import { Component, OnInit, signal, computed, inject, ViewChild, ElementRef } from '@angular/core';
import { TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AdminService, OvertimeRuleDto, ToastService, ConfirmService, StaffStore } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputNumber } from 'primeng/inputnumber';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { Message } from 'primeng/message';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';
import { Skeleton } from 'primeng/skeleton';

@Component({
  selector: 'app-overtime',
  standalone: true,
  imports: [TitleCasePipe, FormsModule, TranslatePipe,
    Button, InputNumber, TableModule, Tag, Card, TooltipModule, Message, ToggleSwitch, Menu, Skeleton],
  templateUrl: './overtime.component.html',
  styleUrl: './overtime.component.scss',
})
export class OvertimeComponent implements OnInit {
  private adminSvc    = inject(AdminService);
  private toast       = inject(ToastService);
  private confirmSvc  = inject(ConfirmService);
  private translate   = inject(TranslateService);
  readonly staffStore = inject(StaffStore);

  @ViewChild('formAnchor', { read: ElementRef }) private formAnchor?: ElementRef<HTMLElement>;
  @ViewChild('rowMenu') rowMenu!: Menu;

  rules     = signal<OvertimeRuleDto[]>([]);
  loading   = signal(false);
  saving    = signal(false);
  showForm  = signal(false);
  editingId = signal<string | null>(null);
  menuItems = signal<MenuItem[]>([]);

  openMenu(rule: OvertimeRuleDto, event: Event): void {
    this.menuItems.set([
      { label: this.translate.instant('common.edit'),   icon: 'pi pi-pencil', command: () => this.openEdit(rule) },
      { separator: true },
      { label: this.translate.instant('common.delete'), icon: 'pi pi-trash',  styleClass: 'nc-danger-item', command: () => this.delete(rule.id) },
    ]);
    this.rowMenu.toggle(event);
  }

  form = { thresholdHours: 8, multiplier: 1.5, isActive: true };

  // ── Multi-role selector ────────────────────────────────────────────────────
  selectedRoles   = signal<string[]>(['*']);
  showRoleDropdown = signal(false);

  roleLabel = computed(() => {
    const s = this.selectedRoles();
    if (s.includes('*')) return 'All Roles (*)';
    if (s.length === 0)  return '— Select roles —';
    return s.map(r => r.charAt(0).toUpperCase() + r.slice(1)).join(', ');
  });

  isRoleSelected(role: string): boolean {
    return this.selectedRoles().includes(role);
  }

  toggleRole(role: string): void {
    this.selectedRoles.update(current => {
      if (role === '*') return ['*'];
      const without = current.filter(r => r !== '*' && r !== role);
      const hadRole = current.includes(role);
      const next    = hadRole ? without : [...without, role];
      return next.length ? next : ['*']; // fall back to "all" if nothing selected
    });
  }

  closeRoleDropdown(): void { this.showRoleDropdown.set(false); }

  ngOnInit() {
    this.staffStore.loadRoles();
    this.load();
  }

  load() {
    this.loading.set(true);
    this.adminSvc.getOvertimeRules().subscribe({
      next:  r  => { this.rules.set(r); this.loading.set(false); },
      error: () => { this.toast.error('Failed to load rules.'); this.loading.set(false); },
    });
  }

  private scrollToForm(): void {
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  openAdd(): void {
    this.editingId.set(null);
    this.form = { thresholdHours: 8, multiplier: 1.5, isActive: true };
    this.selectedRoles.set(['*']);
    this.showRoleDropdown.set(false);
    this.showForm.set(true);
    this.scrollToForm();
  }

  openEdit(rule: OvertimeRuleDto): void {
    this.editingId.set(rule.id);
    this.form = {
      thresholdHours: rule.thresholdHours,
      multiplier: rule.multiplier,
      isActive: rule.isActive,
    };
    const roles = rule.role === '*'
      ? ['*']
      : rule.role.split(',').map(r => r.trim()).filter(Boolean);
    this.selectedRoles.set(roles.length ? roles : ['*']);
    this.showRoleDropdown.set(false);
    this.showForm.set(true);
    this.scrollToForm();
  }

  closeForm(): void {
    this.showForm.set(false);
    this.editingId.set(null);
    this.showRoleDropdown.set(false);
  }

  save() {
    if (this.saving()) return;
    if (this.form.thresholdHours <= 0) {
      this.toast.error('Threshold hours must be greater than zero.'); return;
    }
    if (this.form.multiplier < 1) {
      this.toast.error('Multiplier must be at least 1.0 (cannot reduce pay).'); return;
    }
    const role = this.selectedRoles().includes('*')
      ? '*'
      : this.selectedRoles().join(',');
    this.saving.set(true);
    const payload = { ...this.form, role, id: this.editingId() ?? undefined };
    this.adminSvc.upsertOvertimeRule(payload).subscribe({
      next:  () => {
        this.saving.set(false);
        this.closeForm();
        this.load();
        this.toast.success(this.editingId() ? 'Rule updated.' : 'Rule saved.');
      },
      error: () => { this.toast.error('Save failed.'); this.saving.set(false); },
    });
  }

  delete(id: string) {
    this.confirmSvc.confirm({ title: 'Delete Overtime Rule', message: 'Are you sure you want to delete this overtime rule?', confirmLabel: 'Delete', danger: true })
      .subscribe(ok => {
        if (!ok) return;
        this.adminSvc.deleteOvertimeRule(id).subscribe({
          next:  () => { this.load(); this.toast.success('Rule deleted.'); },
          error: () => this.toast.error('Delete failed.'),
        });
      });
  }
}
