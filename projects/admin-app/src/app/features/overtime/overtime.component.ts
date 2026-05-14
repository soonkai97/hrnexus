import { Component, OnInit, signal, computed, inject, ViewChild, ElementRef } from '@angular/core';
import { CommonModule, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { AdminService, OvertimeRuleDto, ToastService, ConfirmService, StaffStore, TooltipDirective } from 'shared-lib';

@Component({
  selector: 'app-overtime',
  standalone: true,
  imports: [CommonModule, FormsModule, TitleCasePipe, TranslatePipe, TooltipDirective],
  templateUrl: './overtime.component.html',
  styleUrl: './overtime.component.scss',
})
export class OvertimeComponent implements OnInit {
  private adminSvc   = inject(AdminService);
  private toast      = inject(ToastService);
  private confirmSvc = inject(ConfirmService);
  readonly staffStore = inject(StaffStore);

  @ViewChild('formAnchor') private formAnchor?: ElementRef<HTMLElement>;

  rules     = signal<OvertimeRuleDto[]>([]);
  loading   = signal(false);
  saving    = signal(false);
  showForm  = signal(false);
  editingId = signal<string | null>(null);
  openMenuId  = signal<string | null>(null);
  menuPos     = signal<{ top: number; right: number } | null>(null);
  menuTarget  = computed(() => this.rules().find(r => r.id === this.openMenuId()) ?? null);

  toggleMenu(id: string, event: MouseEvent): void {
    if (this.openMenuId() === id) { this.closeMenu(); return; }
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const right = window.innerWidth - rect.right;
    this.menuPos.set({ top: rect.bottom + 4, right });
    this.openMenuId.set(id);
  }

  closeMenu(): void { this.openMenuId.set(null); this.menuPos.set(null); }

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
    this.confirmSvc.confirm({ title: 'Delete Rule', message: 'Remove this OT rule?', confirmLabel: 'Delete', danger: true })
      .subscribe(ok => {
        if (!ok) return;
        this.adminSvc.deleteOvertimeRule(id).subscribe({
          next:  () => { this.load(); this.toast.success('Rule deleted.'); },
          error: () => this.toast.error('Delete failed.'),
        });
      });
  }
}
