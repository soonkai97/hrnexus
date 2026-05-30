import { Component, OnInit, signal, computed, inject, ViewChild, ElementRef } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { StaffStore, ConfirmService, ToastService, TrimInputDirective, CanComponentDeactivate,
         PermissionAdminService, FeatureFlagService, PERMISSION_FEATURE_FLAG } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { Message } from 'primeng/message';
import { Dialog } from 'primeng/dialog';
import { Checkbox } from 'primeng/checkbox';
import { Menu } from 'primeng/menu';
import { MenuItem } from 'primeng/api';

@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe, TrimInputDirective,
    Button, InputText, InputNumber, TableModule, Tag, Card, TooltipModule, Message, Dialog, Checkbox, Menu],
  templateUrl: './roles.component.html',
  styleUrl: './roles.component.scss',
})
export class RolesComponent implements OnInit, CanComponentDeactivate {
  private readonly store      = inject(StaffStore);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly toast      = inject(ToastService);
  private readonly translate  = inject(TranslateService);
  private readonly permSvc    = inject(PermissionAdminService);
  private readonly featureSvc = inject(FeatureFlagService);

  @ViewChild('formAnchor', { read: ElementRef }) private formAnchor?: ElementRef<HTMLElement>;
  @ViewChild('roleMenu') private roleMenu!: Menu;
  roleMenuItems = signal<MenuItem[]>([]);

  readonly roles   = this.store.roles;
  readonly loading = this.store.loading;

  showForm    = signal(false);
  newRoleName = '';
  newRoleRate = 0;
  saving      = signal(false);
  error       = signal('');
  deletingRole = signal<string | null>(null);

  // Inline rate editing state
  editingRateName  = signal<string | null>(null);
  editingRateValue = signal(0);
  savingRate       = signal(false);

  // Role-permissions editor state
  permRole        = signal<string | null>(null);   // role whose permissions dialog is open
  permCatalog     = signal<string[]>([]);          // all assignable permission keys
  permSelected    = signal<string[]>([]);          // currently checked keys
  permLoading     = signal(false);
  permSaving      = signal(false);

  /** Catalog keys to SHOW — hide permissions whose feature flag is disabled. Hidden keys that the
  /// role already has stay in permSelected and are preserved on save (display-only filter). */
  readonly visiblePermCatalog = computed(() =>
    this.permCatalog().filter(k => {
      const flag = PERMISSION_FEATURE_FLAG[k];
      return !flag || this.featureSvc.isEnabled(flag);
    }));

  readonly touched = signal<Set<string>>(new Set());
  markTouched(field: string): void { this.touched.update(s => new Set(s).add(field)); }
  fieldError(field: 'name'): string {
    if (!this.touched().has(field)) return '';
    return this.newRoleName?.trim() ? '' : this.translate.instant('roles.nameRequired');
  }

  ngOnInit(): void { this.store.loadRoles(); }

  openAdd(): void {
    this.newRoleName = '';
    this.newRoleRate = 0;
    this.error.set('');
    this.touched.set(new Set());
    this.showForm.set(true);
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  cancel(): void { this.showForm.set(false); this.error.set(''); this.touched.set(new Set()); }

  addRole(): void {
    const name = this.newRoleName.trim().toUpperCase();
    if (!name) return;
    this.saving.set(true);
    this.error.set('');
    this.store.createRole(name, this.newRoleRate).subscribe({
      next: () => {
        this.newRoleName = '';
        this.newRoleRate = 0;
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.success(this.translate.instant('roles.created'));
      },
      error: (e) => { this.error.set(e?.error?.error ?? this.translate.instant('roles.createFailed')); this.saving.set(false); },
    });
  }

  startEditRate(name: string, currentRate: number): void {
    this.editingRateName.set(name);
    this.editingRateValue.set(currentRate);
  }

  cancelEditRate(): void { this.editingRateName.set(null); }

  saveRate(): void {
    const name = this.editingRateName();
    if (!name || this.savingRate()) return;
    this.savingRate.set(true);
    this.store.updateRoleRate(name, this.editingRateValue()).subscribe({
      next: () => {
        this.savingRate.set(false);
        this.editingRateName.set(null);
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
           this.editingRateName() !== null;
  }

  deleteRole(name: string): void {
    this.confirmSvc.confirm({
      title: this.translate.instant('roles.confirmDeleteTitle'),
      message: this.translate.instant('roles.confirmDeleteMessage'),
      confirmLabel: this.translate.instant('common.delete'),
      danger: true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.deletingRole.set(name);
      this.store.deleteRole(name).subscribe({
        next: () => { this.deletingRole.set(null); this.toast.success(this.translate.instant('roles.deleted')); },
        error: (e) => { this.deletingRole.set(null); this.error.set(e?.error?.error ?? this.translate.instant('roles.deleteFailed')); },
      });
    });
  }

  // ── Role permissions editor ────────────────────────────────────────────────

  /** Per-row actions via the ellipsis menu (Delete only for custom roles). */
  openRoleMenu(role: { name: string; hourlyRate: number; isSystem: boolean }, event: Event): void {
    const items: MenuItem[] = [
      { label: this.translate.instant('roles.editRate'), icon: 'pi pi-pencil',
        command: () => this.startEditRate(role.name, role.hourlyRate) },
      { label: this.translate.instant('permissions.roleTitle'), icon: 'pi pi-shield',
        command: () => this.openPermissions(role.name) },
    ];
    if (!role.isSystem) {
      items.push({ separator: true });
      items.push({ label: this.translate.instant('roles.deleteRole'), icon: 'pi pi-trash',
        styleClass: 'nc-danger-item', command: () => this.deleteRole(role.name) });
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
