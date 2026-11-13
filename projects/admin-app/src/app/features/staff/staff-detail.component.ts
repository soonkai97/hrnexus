import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { StaffDetail, UpdateStaffRequest, StaffStore, StaffManagementService, ToastService, ConfirmService, TrimInputDirective, CanComponentDeactivate, apiErrorMessage, PermissionAdminService, StaffOverride, groupedPermissions, permissionLabel, permissionDescription } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Skeleton } from 'primeng/skeleton';
import { DatePicker } from 'primeng/datepicker';
import { MultiSelect } from 'primeng/multiselect';
import { SelectButton } from 'primeng/selectbutton';
import { Tabs, TabList, Tab } from 'primeng/tabs';

/** Tri-state choice for a per-user permission override. */
type OverrideChoice = 'inherit' | 'grant' | 'revoke';
/** Which detail tab is active. */
type DetailTab = 'profile' | 'permissions';

@Component({
  selector: 'app-staff-detail',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink, TrimInputDirective,
    Button, InputText, Tag, Card, Skeleton, DatePicker, MultiSelect, SelectButton, Tabs, TabList, Tab],
  templateUrl: './staff-detail.component.html',
  styleUrl: './staff-detail.component.scss',
})
export class StaffDetailComponent implements OnInit, CanComponentDeactivate {
  readonly staffStore  = inject(StaffStore);
  private readonly staffSvc    = inject(StaffManagementService);
  private readonly toast       = inject(ToastService);
  private readonly confirmSvc  = inject(ConfirmService);
  private readonly route       = inject(ActivatedRoute);
  private readonly permSvc     = inject(PermissionAdminService);

  staff            = signal<StaffDetail | null>(null);
  loading          = signal(true);
  editMode         = signal(false);
  saving           = signal(false);
  activeTab        = signal<DetailTab>('profile');

  /** Managers now have the same power as admins, so role editing is never hidden. */
  readonly isManager = computed(() => false);

  // Every defined role is assignable (incl. SUPERADMIN); managers and admins have equal power.
  availableRoles = computed(() => this.staffStore.roles());

  // Pickers use the GUID id (the edit-staff DTO submits role IDS).
  readonly roleOptions     = computed(() => this.availableRoles().map(r => ({ label: r.name, value: r.id })));

  /** Map a role NAME (as stored on the staff record) to its GUID id via the loaded roles. */
  private roleIdForName(name: string): string {
    return this.availableRoles().find(r => r.name === name)?.id ?? '';
  }

  editForm: UpdateStaffRequest = {
    staffId: '', firstName: '', lastName: '', email: null, roles: [],
    phoneNumber: null, icNo: null, passportNo: null, dateOfBirth: null,
  };
  private _initialEditForm: UpdateStaffRequest = { ...this.editForm, roles: [] };

  private staffId = '';

  // ── Per-user permission overrides ───────────────────────────────────────────
  permKeys     = signal<string[]>([]);
  /** permissionKey → tri-state choice; defaults to 'inherit' for every catalog key. */
  permChoices  = signal<Record<string, OverrideChoice>>({});
  permLoading  = signal(true);
  permSaving   = signal(false);
  permEditMode = signal(false);
  /** Snapshot of choices taken on load / entering edit — basis for dirty detection + Cancel. */
  permInitial  = signal<Record<string, OverrideChoice>>({});

  /** Save is enabled only when at least one choice differs from the snapshot. */
  readonly permDirty = computed(() => {
    const cur = this.permChoices(), init = this.permInitial();
    return this.permKeys().some(k => (cur[k] ?? 'inherit') !== (init[k] ?? 'inherit'));
  });
  /** Catalog keys organised into labelled sections for the edit list. */
  readonly visiblePermGroups = computed(() => groupedPermissions(this.permKeys()));
  readonly permLabel = permissionLabel;
  readonly permDesc = permissionDescription;

  /** Keys with a non-inherit choice — what the read-only view lists. */
  readonly activeOverrides = computed(() =>
    this.permKeys().filter(k => (this.permChoices()[k] ?? 'inherit') !== 'inherit'));

  /** Options for each tri-state p-selectButton. */
  readonly permOptions: { label: string; value: OverrideChoice }[] = [
    { label: 'Inherit', value: 'inherit' },
    { label: 'Grant',   value: 'grant'   },
    { label: 'Revoke',  value: 'revoke'  },
  ];

  permChoiceLabel(c: OverrideChoice | undefined): string { return { inherit: 'Inherit', grant: 'Grant', revoke: 'Revoke' }[c ?? 'inherit']; }
  permChoiceSeverity(c: OverrideChoice | undefined): 'success' | 'danger' | 'secondary' {
    return c === 'grant' ? 'success' : c === 'revoke' ? 'danger' : 'secondary';
  }
  startPermEdit():  void { this.permInitial.set({ ...this.permChoices() }); this.permEditMode.set(true); }
  cancelPermEdit(): void { this.permChoices.set({ ...this.permInitial() }); this.permEditMode.set(false); }

  ngOnInit(): void {
    this.staffId = this.route.snapshot.paramMap.get('id') ?? '';
    this.staffStore.loadRoles();
    this.loadStaff();
    this.loadPermissions();
  }

  loadStaff(): void {
    this.loading.set(true);
    this.staffSvc.getById(this.staffId).subscribe({
      next:  d  => { this.staff.set(d); this.loading.set(false); },
      error: () => { this.toast.error('Failed to load staff details.'); this.loading.set(false); },
    });
  }

  startEdit(): void {
    const s = this.staff()!;
    this.editForm = {
      staffId: s.staffId, firstName: s.firstName, lastName: s.lastName,
      email: s.email || null,
      // s.roles are NAMES (display) but the update DTO + role picker use IDS — map name → id.
      roles: s.roles.map(name => this.roleIdForName(name)).filter(id => id),
      phoneNumber: s.phoneNumber,
      icNo: s.icNo ?? null, passportNo: s.passportNo ?? null,
      dateOfBirth: s.dateOfBirth ?? null,
    };
    this._initialEditForm = { ...this.editForm, roles: [...this.editForm.roles] };
    this.touched.set(new Set());
    this.editMode.set(true);
  }

  hasUnsavedChanges(): boolean {
    if (!this.editMode()) return false;
    return JSON.stringify(this.editForm) !== JSON.stringify(this._initialEditForm);
  }

  /** Gates the Save button — only Staff ID and first name are required (email must be valid if present). */
  get isEditFormValid(): boolean {
    return !!this.editForm.staffId?.trim() &&
      !!this.editForm.firstName?.trim() &&
      this.emailIsValid();
  }

  // ── Per-field inline validation ───────────────────────────────────────────────
  private readonly emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
  readonly touched = signal<Set<string>>(new Set());

  private emailIsValid(): boolean {
    const e = this.editForm.email?.trim();
    return !e || this.emailRegex.test(e);
  }

  markTouched(field: string): void {
    this.touched.update(s => new Set(s).add(field));
  }

  /** Inline error text for a field — empty when valid or not yet blurred. */
  fieldError(field: 'staffId' | 'firstName' | 'email'): string {
    if (!this.touched().has(field)) return '';
    const f = this.editForm;
    switch (field) {
      case 'staffId':   return f.staffId?.trim()   ? '' : 'Staff ID is required.';
      case 'firstName': return f.firstName?.trim() ? '' : 'First name is required.';
      case 'email':     return this.emailIsValid() ? '' : 'Enter a valid email address.';
    }
  }

  cancelEdit(): void {
    if (this.hasUnsavedChanges()) {
      this.confirmSvc.confirm({
        title: 'Discard Changes',
        message: 'You have unsaved changes. Discard them?',
        confirmLabel: 'Discard',
        danger: true,
      }).subscribe(ok => { if (ok) this.editMode.set(false); });
    } else {
      this.editMode.set(false);
    }
  }

  saveEdit(): void {
    this.saving.set(true);
    // Coerce empty-string DOB to null — the API field is DateTime? and rejects "".
    const req: UpdateStaffRequest = {
      ...this.editForm,
      dateOfBirth: this.editForm.dateOfBirth?.trim() || null,
    };
    this.staffStore.update(this.staffId, req).subscribe({
      next:  ()       => { this.loadStaff(); this.editMode.set(false); this.saving.set(false); this.toast.success('Profile updated.'); },
      error: (e: unknown) => { this.toast.error(apiErrorMessage(e) ?? 'Update failed.'); this.saving.set(false); },
    });
  }

  // ── Permission overrides ────────────────────────────────────────────────────

  /** Loads the catalog of permission keys, then seeds each tri-state from stored overrides. */
  loadPermissions(): void {
    this.permLoading.set(true);
    this.permSvc.catalog().subscribe({
      next: keys => {
        this.permKeys.set(keys);
        this.permSvc.getUserOverrides(this.staffId).subscribe({
          next: overrides => {
            const choices: Record<string, OverrideChoice> = {};
            for (const k of keys) choices[k] = 'inherit';
            for (const o of overrides) choices[o.permissionKey] = o.granted ? 'grant' : 'revoke';
            this.permChoices.set(choices);
            this.permInitial.set({ ...choices });
            this.permLoading.set(false);
          },
          error: () => { this.toast.error('Failed to load permissions.'); this.permLoading.set(false); },
        });
      },
      error: () => { this.toast.error('Failed to load permissions.'); this.permLoading.set(false); },
    });
  }

  /** Updates a single key's tri-state choice. Coerces null (p-selectButton deselect) back to 'inherit'. */
  setPermChoice(key: string, value: OverrideChoice | null): void {
    this.permChoices.update(c => ({ ...c, [key]: value ?? 'inherit' }));
  }

  /** Persists only the non-inherit (grant/revoke) choices as StaffOverride[]. */
  savePermissions(): void {
    this.permSaving.set(true);
    const choices = this.permChoices();
    const overrides: StaffOverride[] = this.permKeys()
      .filter(k => choices[k] && choices[k] !== 'inherit')
      .map(k => ({ permissionKey: k, granted: choices[k] === 'grant' }));
    this.permSvc.setUserOverrides(this.staffId, overrides).subscribe({
      next:  () => {
        this.toast.success('Permissions updated.');
        this.permInitial.set({ ...choices });   // new clean baseline
        this.permEditMode.set(false);            // back to view mode
        this.permSaving.set(false);
      },
      error: () => { this.toast.error('Failed to save permissions.'); this.permSaving.set(false); },
    });
  }
}
