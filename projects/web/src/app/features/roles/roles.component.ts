import { Component, OnInit, signal, computed, inject, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { StaffStore, ConfirmService, ToastService, TrimInputDirective, CanComponentDeactivate,
         PermissionAdminService, groupedPermissions, permissionLabel, permissionDescription, ViewModeService, ViewToggleComponent } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { Dialog } from 'primeng/dialog';
import { Checkbox } from 'primeng/checkbox';
import { Menu } from 'primeng/menu';
import { Paginator } from 'primeng/paginator';
import { MenuItem } from 'primeng/api';

@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [FormsModule, TrimInputDirective,
    Button, InputText, TableModule, Tag, Card, TooltipModule, Dialog, Checkbox, Menu, Paginator, ViewToggleComponent],
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

  private readonly views = inject(ViewModeService);
  /** Cards (default) or Table, remembered on this device. */
  readonly viewMode = this.views.mode('roles');

  readonly roles   = this.store.roles;

  // Card view paging: the same roles as the table, by name, a page at a time.
  readonly cardRows = 12;
  readonly cardFirst = signal(0);
  readonly pagedRoles = computed(() => {
    const sorted = [...this.roles()].sort((a, b) => a.name.localeCompare(b.name));
    const first = this.cardFirst() >= sorted.length ? 0 : this.cardFirst();
    return sorted.slice(first, first + this.cardRows);
  });
  readonly loading = this.store.loading;

  showForm       = signal(false);
  newRoleName    = '';
  saving         = signal(false);
  deletingRole   = signal<string | null>(null);

  // Rename-role modal state — keyed by the role's GUID id. Only custom roles can be renamed: a system
  // role's name is its authorization key, so it is never editable.
  editingId   = signal<string | null>(null);
  editingName = signal('');
  savingEdit  = signal(false);

  // Role-permissions editor state
  permRole        = signal<string | null>(null);   // role whose permissions dialog is open
  /** The first administrator role always holds every permission, so its dialog is read-only. */
  permReadOnly    = signal(false);
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
    return this.newRoleName?.trim() ? '' : 'Role name is required.';
  }

  ngOnInit(): void { this.store.loadRoles(); }

  openAdd(): void {
    this.newRoleName = '';
    this.touched.set(new Set());
    this.showForm.set(true);
  }

  cancel(): void { this.showForm.set(false); this.touched.set(new Set()); }

  addRole(): void {
    const name = this.newRoleName.trim().toUpperCase();
    if (!name) return;
    this.saving.set(true);
    this.store.createRole(name).subscribe({
      next: () => {
        this.newRoleName = '';
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.success('Role created.');
      },
      error: (e) => { this.toast.error(e?.error?.error ?? 'Failed to create role.'); this.saving.set(false); },
    });
  }

  /** Open the rename modal, prefilled from the row. Every role can be renamed: nothing looks a role up by its name. */
  startEdit(role: { id: string; name: string; isSystem: boolean }): void {
    this.editingId.set(role.id);
    this.editingName.set(role.name);
  }

  cancelEdit(): void { this.editingId.set(null); }

  saveEdit(): void {
    const id = this.editingId();
    const name = this.editingName().trim().toUpperCase();
    if (!id || !name || this.savingEdit()) return;
    this.savingEdit.set(true);
    this.store.renameRole(id, name).subscribe({
      next: () => {
        this.savingEdit.set(false);
        this.editingId.set(null);
        this.toast.success('Role updated.');
      },
      error: (e) => {
        this.toast.error(e?.error?.error ?? 'Failed to update role.');
        this.savingEdit.set(false);
      },
    });
  }

  hasUnsavedChanges(): boolean {
    return (this.showForm() && !!this.newRoleName.trim()) || this.editingId() !== null;
  }

  deleteRole(id: string): void {
    this.confirmSvc.confirm({
      title: 'Delete Role',
      message: 'Deleting this role will remove it from all staff members. Are you sure?',
      confirmLabel: 'Delete',
      danger: true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.deletingRole.set(id);
      this.store.deleteRole(id).subscribe({
        next: () => { this.deletingRole.set(null); this.toast.success('Role deleted.'); },
        error: (e) => { this.deletingRole.set(null); this.toast.error(e?.error?.error ?? 'Failed to delete role.'); },
      });
    });
  }

  // ── Role permissions editor ────────────────────────────────────────────────

  /** Per-row actions via the ellipsis menu (every role can be renamed; only custom roles can be deleted). */
  openRoleMenu(role: { id: string; name: string; isSystem: boolean }, event: Event): void {
    const items: MenuItem[] = [
      { label: 'Edit', icon: 'pi pi-pencil',
        command: () => this.startEdit(role) },
      { label: 'Role permissions', icon: 'pi pi-shield',
        // Permissions API is keyed by role NAME on the backend — pass the name here, not the id.
        command: () => this.openPermissions(role.name, role.isSystem) },
    ];
    if (!role.isSystem) {
      items.push({ separator: true });
      items.push({ label: 'Delete', icon: 'pi pi-trash',
        styleClass: 'nc-danger-item', command: () => this.deleteRole(role.id) });
    }
    this.roleMenuItems.set(items);
    this.roleMenu.toggle(event);
  }

  openPermissions(name: string, readOnly = false): void {
    this.permRole.set(name);
    this.permReadOnly.set(readOnly);
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
    if (!role || this.permSaving() || this.permReadOnly()) return;
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
