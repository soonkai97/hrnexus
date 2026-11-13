import { Component, OnInit, signal, computed, inject, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { StaffStore, ConfirmService, ToastService, TrimInputDirective, CanComponentDeactivate,
         PermissionAdminService, groupedPermissions, LocalizedNamePipe, PayType } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { Dialog } from 'primeng/dialog';
import { Checkbox } from 'primeng/checkbox';
import { Menu } from 'primeng/menu';
import { SelectButton } from 'primeng/selectbutton';
import { MenuItem } from 'primeng/api';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-roles',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, TrimInputDirective, LocalizedNamePipe,
    Button, InputText, InputNumber, TableModule, Tag, Card, TooltipModule, Dialog, Checkbox, Menu, SelectButton],
  templateUrl: './roles.component.html',
  styleUrl: './roles.component.scss',
})
export class RolesComponent implements OnInit, CanComponentDeactivate {
  private readonly store      = inject(StaffStore);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly toast      = inject(ToastService);
  private readonly translate  = inject(TranslateService);
  private readonly permSvc    = inject(PermissionAdminService);

  @ViewChild('roleMenu') private roleMenu!: Menu;
  roleMenuItems = signal<MenuItem[]>([]);

  readonly roles   = this.store.roles;
  readonly loading = this.store.loading;

  showForm       = signal(false);
  newRoleName    = '';
  newRoleNameZh  = '';
  newRoleNameVi  = '';
  newRoleRate    = 0;
  newRolePayType: PayType = 'hourly';
  saving         = signal(false);
  deletingRole   = signal<string | null>(null);

  /** Pay-type toggle options for the SelectButton. A getter so labels re-translate on language change. */
  get payTypeOptions(): { label: string; value: PayType }[] {
    return [
      { label: this.translate.instant('roles.payHourly'), value: 'hourly' },
      { label: this.translate.instant('roles.payFixed'),  value: 'fixed'  },
    ];
  }

  // Edit-role modal state — keyed by the role's GUID id (roles are id-identified since the GUID-PK
  // migration, so the English name is now editable display data — except for system roles, whose name
  // is the authorization key and stays locked).
  editingRateId       = signal<string | null>(null);
  editingRateName     = signal('');
  editingRateNameZh   = signal('');
  editingRateNameVi   = signal('');
  editingRateValue    = signal(0);
  editingRatePayType  = signal<PayType>('hourly');
  editingRateIsSystem = signal(false);
  savingRate          = signal(false);

  // Role-permissions editor state
  permRole        = signal<string | null>(null);   // role whose permissions dialog is open
  permCatalog     = signal<string[]>([]);          // all assignable permission keys
  permSelected    = signal<string[]>([]);          // currently checked keys
  permLoading     = signal(false);
  permSaving      = signal(false);

  /** Catalog organised into labelled sections for the dialog. */
  readonly visiblePermGroups = computed(() => groupedPermissions(this.permCatalog()));

  readonly touched = signal<Set<string>>(new Set());
  markTouched(field: string): void { this.touched.update(s => new Set(s).add(field)); }
  fieldError(field: 'name'): string {
    if (!this.touched().has(field)) return '';
    return this.newRoleName?.trim() ? '' : this.translate.instant('roles.nameRequired');
  }

  ngOnInit(): void { this.store.loadRoles(); }

  openAdd(): void {
    this.newRoleName = '';
    this.newRoleNameZh = '';
    this.newRoleNameVi = '';
    this.newRoleRate = 0;
    this.newRolePayType = 'hourly';
    this.touched.set(new Set());
    this.showForm.set(true);
  }

  cancel(): void { this.showForm.set(false); this.touched.set(new Set()); }

  addRole(): void {
    const name = this.newRoleName.trim().toUpperCase();
    if (!name) return;
    this.saving.set(true);
    this.store.createRole(name, this.newRoleRate, this.newRoleNameZh.trim() || null, this.newRoleNameVi.trim() || null, this.newRolePayType).subscribe({
      next: () => {
        this.newRoleName = '';
        this.newRoleNameZh = '';
        this.newRoleNameVi = '';
        this.newRoleRate = 0;
        this.newRolePayType = 'hourly';
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.success(this.translate.instant('roles.created'));
      },
      error: (e) => { this.toast.error(e?.error?.error ?? this.translate.instant('roles.createFailed')); this.saving.set(false); },
    });
  }

  /** Open the edit-role modal, prefilled from the row. `isSystem` locks the English-name field. */
  startEditRate(role: { id: string; name: string; hourlyRate: number; nameZh: string | null; nameVi: string | null; payType: PayType; isSystem: boolean }): void {
    this.editingRateId.set(role.id);
    this.editingRateName.set(role.name);
    this.editingRateNameZh.set(role.nameZh ?? '');
    this.editingRateNameVi.set(role.nameVi ?? '');
    this.editingRateValue.set(role.hourlyRate);
    this.editingRatePayType.set(role.payType);
    this.editingRateIsSystem.set(role.isSystem);
  }

  cancelEditRate(): void { this.editingRateId.set(null); }

  saveRate(): void {
    const id = this.editingRateId();
    if (!id || this.savingRate()) return;
    // System roles keep their English name (the auth key) — only send a name change for custom jobs.
    const name = this.editingRateIsSystem() ? null : (this.editingRateName().trim().toUpperCase() || null);
    if (!this.editingRateIsSystem() && !name) return; // English name is required for custom jobs
    this.savingRate.set(true);
    this.store.updateRoleRate(id, this.editingRateValue(), this.editingRateNameZh().trim() || null, this.editingRateNameVi().trim() || null, this.editingRatePayType(), name).subscribe({
      next: () => {
        this.savingRate.set(false);
        this.editingRateId.set(null);
        this.toast.success(this.translate.instant('roles.rateUpdated'));
      },
      error: (e) => {
        this.toast.error(e?.error?.error ?? this.translate.instant('roles.rateUpdateFailed'));
        this.savingRate.set(false);
      },
    });
  }

  hasUnsavedChanges(): boolean {
    return (this.showForm() && (!!this.newRoleName.trim() || this.newRoleRate > 0)) ||
           this.editingRateId() !== null;
  }

  deleteRole(id: string): void {
    this.confirmSvc.confirm({
      title: this.translate.instant('roles.confirmDeleteTitle'),
      message: this.translate.instant('roles.confirmDeleteMessage'),
      confirmLabel: this.translate.instant('common.delete'),
      danger: true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.deletingRole.set(id);
      this.store.deleteRole(id).subscribe({
        next: () => { this.deletingRole.set(null); this.toast.success(this.translate.instant('roles.deleted')); },
        error: (e) => { this.deletingRole.set(null); this.toast.error(e?.error?.error ?? this.translate.instant('roles.deleteFailed')); },
      });
    });
  }

  // ── Role permissions editor ────────────────────────────────────────────────

  /** Per-row actions via the ellipsis menu (Delete only for custom roles). */
  openRoleMenu(role: { id: string; name: string; hourlyRate: number; isSystem: boolean; nameZh: string | null; nameVi: string | null; payType: PayType }, event: Event): void {
    const items: MenuItem[] = [
      { label: this.translate.instant('roles.editRate'), icon: 'pi pi-pencil',
        command: () => this.startEditRate(role) },
      { label: this.translate.instant('permissions.roleTitle'), icon: 'pi pi-shield',
        // Permissions API is keyed by role NAME on the backend — pass the name here, not the id.
        command: () => this.openPermissions(role.name) },
    ];
    if (!role.isSystem) {
      items.push({ separator: true });
      items.push({ label: this.translate.instant('roles.deleteRole'), icon: 'pi pi-trash',
        styleClass: 'nc-danger-item', command: () => this.deleteRole(role.id) });
    }
    this.roleMenuItems.set(items);
    this.roleMenu.toggle(event);
  }

  openPermissions(name: string): void {
    this.permRole.set(name);
    this.permSelected.set([]);
    this.permLoading.set(true);
    // Catalog is fetched once and cached on the component.
    if (this.permCatalog().length === 0) {
      this.permSvc.catalog().subscribe({
        next: (keys) => this.permCatalog.set(keys),
        error: () => this.toast.error(this.translate.instant('permissions.loadFailed')),
      });
    }
    this.permSvc.getRolePermissions(name).subscribe({
      next: (keys) => { this.permSelected.set(keys); this.permLoading.set(false); },
      error: () => { this.permLoading.set(false); this.toast.error(this.translate.instant('permissions.loadFailed')); },
    });
  }

  closePermissions(): void {
    this.permRole.set(null);
    this.permSelected.set([]);
  }

  togglePermission(key: string, checked: boolean): void {
    this.permSelected.update(keys =>
      checked ? [...keys, key] : keys.filter(k => k !== key));
  }

  savePermissions(): void {
    const role = this.permRole();
    if (!role || this.permSaving()) return;
    this.permSaving.set(true);
    this.permSvc.setRolePermissions(role, this.permSelected()).subscribe({
      next: () => {
        this.permSaving.set(false);
        this.toast.success(this.translate.instant('permissions.saved'));
        this.closePermissions();
      },
      error: () => {
        this.permSaving.set(false);
        this.toast.error(this.translate.instant('permissions.saveFailed'));
      },
    });
  }
}
