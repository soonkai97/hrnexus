import { Component, OnInit, signal, computed, inject, ViewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StaffStore, ConfirmService, ToastService, TrimInputDirective, CanComponentDeactivate,
         PermissionAdminService, groupedPermissions, permissionLabel, permissionDescription, PayType } from 'shared-lib';
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
  selector: 'app-roles',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TrimInputDirective,
    Button, InputText, InputNumber, TableModule, Tag, Card, TooltipModule, Dialog, Checkbox, Menu, SelectButton],
  templateUrl: './roles.component.html',
  styleUrl: './roles.component.scss',
})
export class RolesComponent implements OnInit, CanComponentDeactivate {
  private readonly store      = inject(StaffStore);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly toast      = inject(ToastService);
  private readonly permSvc    = inject(PermissionAdminService);

  @ViewChild('roleMenu') private roleMenu!: Menu;
  roleMenuItems = signal<MenuItem[]>([]);

  readonly roles   = this.store.roles;
  readonly loading = this.store.loading;

  showForm       = signal(false);
  newRoleName    = '';
  newRoleRate    = 0;
  newRolePayType: PayType = 'hourly';
  saving         = signal(false);
  deletingRole   = signal<string | null>(null);

  /** Pay-type toggle options for the SelectButton. */
  readonly payTypeOptions: { label: string; value: PayType }[] = [
    { label: 'Hourly', value: 'hourly' },
    { label: 'Fixed per shift',  value: 'fixed'  },
  ];

  // Edit-role modal state — keyed by the role's GUID id (roles are id-identified since the GUID-PK
  // migration, so the English name is now editable display data — except for system roles, whose name
  // is the authorization key and stays locked).
  editingRateId       = signal<string | null>(null);
  editingRateName     = signal('');
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
  readonly permLabel = permissionLabel;
  readonly permDesc = permissionDescription;

  readonly touched = signal<Set<string>>(new Set());
  markTouched(field: string): void { this.touched.update(s => new Set(s).add(field)); }
  fieldError(field: 'name'): string {
    if (!this.touched().has(field)) return '';
    return this.newRoleName?.trim() ? '' : 'Job name is required.';
  }

  ngOnInit(): void { this.store.loadRoles(); }

  openAdd(): void {
    this.newRoleName = '';
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
    this.store.createRole(name, this.newRoleRate, this.newRolePayType).subscribe({
      next: () => {
        this.newRoleName = '';
        this.newRoleRate = 0;
        this.newRolePayType = 'hourly';
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.success('Job created.');
      },
      error: (e) => { this.toast.error(e?.error?.error ?? 'Failed to create job.'); this.saving.set(false); },
    });
  }

  /** Open the edit-role modal, prefilled from the row. `isSystem` locks the English-name field. */
  startEditRate(role: { id: string; name: string; hourlyRate: number; payType: PayType; isSystem: boolean }): void {
    this.editingRateId.set(role.id);
    this.editingRateName.set(role.name);
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
    this.store.updateRoleRate(id, this.editingRateValue(), this.editingRatePayType(), name).subscribe({
      next: () => {
        this.savingRate.set(false);
        this.editingRateId.set(null);
        this.toast.success('Job updated.');
      },
      error: (e) => {
        this.toast.error(e?.error?.error ?? 'Failed to update job.');
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
      title: 'Delete Job',
      message: 'Deleting this job will remove it from all staff members. Are you sure?',
      confirmLabel: 'Delete',
      danger: true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.deletingRole.set(id);
      this.store.deleteRole(id).subscribe({
        next: () => { this.deletingRole.set(null); this.toast.success('Job deleted.'); },
        error: (e) => { this.deletingRole.set(null); this.toast.error(e?.error?.error ?? 'Failed to delete job.'); },
      });
    });
  }

  // ── Role permissions editor ────────────────────────────────────────────────

  /** Per-row actions via the ellipsis menu (Delete only for custom roles). */
  openRoleMenu(role: { id: string; name: string; hourlyRate: number; isSystem: boolean; payType: PayType }, event: Event): void {
    const items: MenuItem[] = [
      { label: 'Edit', icon: 'pi pi-pencil',
        command: () => this.startEditRate(role) },
      { label: 'Job permissions', icon: 'pi pi-shield',
        // Permissions API is keyed by role NAME on the backend — pass the name here, not the id.
        command: () => this.openPermissions(role.name) },
    ];
    if (!role.isSystem) {
      items.push({ separator: true });
      items.push({ label: 'Delete', icon: 'pi pi-trash',
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
        error: () => this.toast.error('Failed to load permissions.'),
      });
    }
    this.permSvc.getRolePermissions(name).subscribe({
      next: (keys) => { this.permSelected.set(keys); this.permLoading.set(false); },
      error: () => { this.permLoading.set(false); this.toast.error('Failed to load permissions.'); },
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
        this.toast.success('Permissions updated.');
        this.closePermissions();
      },
      error: () => {
        this.permSaving.set(false);
        this.toast.error('Failed to save permissions.');
      },
    });
  }
}
