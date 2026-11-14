import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Checkbox } from 'primeng/checkbox';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { PrimeTemplate } from 'primeng/api';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import {
  CanComponentDeactivate,
  LeaveColour,
  LeavePool,
  LeaveTypeDefinition,
  LeaveTypeService,
  ToastService,
  apiErrorMessage,
} from 'shared-lib';

/** How a type's advance notice is set: none, the company policy's annual-leave notice, or its own days. */
export type NoticeMode = 'none' | 'policy' | 'days';

/** The type being added or edited in the dialog (numbers are nullable while the person types). */
export interface TypeDraft {
  /** Null for a new type; the key of the type being edited otherwise. */
  editingKey: string | null;
  key: string;
  label: string;
  colour: LeaveColour;
  sortOrder: number | null;
  active: boolean;
  isSystem: boolean;
  hasLeave: boolean;
  pool: LeavePool;
  allowanceDays: number | null;
  maxDaysPerRequest: number | null;
  autoApprove: boolean;
  noticeMode: NoticeMode;
  noticeDays: number | null;
  deductsPay: boolean;
  adminOnly: boolean;
  countsCalendarDays: boolean;
  sensitive: boolean;
}

const KEY_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/**
 * The "Leave types" section of the company policy: which kinds of leave the company has and how each
 * behaves. The server enforces every rule; the checks here mirror it so the form says what is wrong before
 * saving. A type that already has leave cannot change where it is charged or how its days are counted, and
 * cannot be removed (it is deactivated instead).
 */
@Component({
  selector: 'app-leave-types-editor',
  standalone: true,
  imports: [FormsModule, Button, Card, Checkbox, Dialog, InputText, Message, PrimeTemplate, Select, Skeleton, Tag],
  templateUrl: './leave-types-editor.component.html',
  styleUrl: './leave-types-editor.component.scss',
})
export class LeaveTypesEditorComponent implements OnInit, CanComponentDeactivate {
  private readonly typeSvc = inject(LeaveTypeService);
  private readonly toast = inject(ToastService);

  readonly colourOptions: { label: string; value: LeaveColour }[] = [
    { label: 'Blue', value: 'info' },
    { label: 'Green', value: 'success' },
    { label: 'Amber', value: 'warn' },
    { label: 'Red', value: 'danger' },
    { label: 'Grey', value: 'secondary' },
    { label: 'Dark', value: 'contrast' },
  ];

  readonly poolOptions: { label: string; value: LeavePool }[] = [
    { label: 'No balance (a manager approves each request)', value: 'none' },
    { label: "The person's annual leave balance", value: 'annual' },
    { label: "The person's sick leave balance", value: 'sick' },
    { label: 'Its own yearly allowance (the same for everyone)', value: 'own' },
  ];

  readonly noticeOptions: { label: string; value: NoticeMode }[] = [
    { label: 'No notice needed', value: 'none' },
    { label: "The company's annual-leave notice", value: 'policy' },
    { label: 'A number of days for this type', value: 'days' },
  ];

  loading = signal(true);
  saving = signal(false);
  error = signal('');
  /** True when the server refused the save because someone else changed the types first. */
  conflict = signal(false);

  /** The catalogue being edited, in sort order. */
  rows = signal<LeaveTypeDefinition[]>([]);
  private version = 0;
  private savedJson = '[]';

  dialogOpen = signal(false);
  dialogError = signal('');
  draft: TypeDraft = this.blankDraft();

  ngOnInit(): void {
    this.load();
  }

  // ── Loading ──────────────────────────────────────────────────────────────────

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.conflict.set(false);
    this.typeSvc.loadAll().subscribe({
      next: (c) => {
        this.version = c.version;
        this.rows.set([...c.types].sort((a, b) => a.sortOrder - b.sortOrder));
        this.savedJson = JSON.stringify(this.rows());
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(apiErrorMessage(err) ?? 'Could not load the leave types.');
        this.loading.set(false);
      },
    });
  }

  hasUnsavedChanges(): boolean {
    return JSON.stringify(this.rows()) !== this.savedJson;
  }

  // ── Describing a type in the list ────────────────────────────────────────────

  balanceText(t: LeaveTypeDefinition): string {
    switch (t.pool) {
      case 'annual':
        return 'Annual balance';
      case 'sick':
        return 'Sick balance';
      case 'own':
        return `${t.allowanceDays} days a year`;
      default:
        return 'No balance';
    }
  }

  limitText(t: LeaveTypeDefinition): string {
    return t.maxDaysPerRequest === null
      ? 'Company limit'
      : `Up to ${t.maxDaysPerRequest} ${t.countsCalendarDays ? 'calendar ' : ''}days`;
  }

  flags(t: LeaveTypeDefinition): string[] {
    const flags: string[] = [];
    if (t.autoApprove) flags.push('Approves itself');
    if (t.advanceNoticeFromPolicy) flags.push('Company notice');
    else if (t.advanceNoticeDays !== null) flags.push(`${t.advanceNoticeDays} days notice`);
    if (t.deductsPay) flags.push('Deducts pay');
    if (t.adminOnly) flags.push('Admin only');
    if (t.countsCalendarDays) flags.push('Calendar days');
    if (t.sensitive) flags.push('Private');
    return flags;
  }

  // ── Adding and editing ───────────────────────────────────────────────────────

  startAdd(): void {
    const next = Math.max(0, ...this.rows().map((r) => r.sortOrder)) + 10;
    this.draft = { ...this.blankDraft(), sortOrder: next };
    this.dialogError.set('');
    this.dialogOpen.set(true);
  }

  startEdit(t: LeaveTypeDefinition): void {
    this.draft = {
      editingKey: t.key,
      key: t.key,
      label: t.label,
      colour: t.colour,
      sortOrder: t.sortOrder,
      active: t.active,
      isSystem: t.isSystem,
      hasLeave: t.hasLeave,
      pool: t.pool,
      allowanceDays: t.allowanceDays,
      maxDaysPerRequest: t.maxDaysPerRequest,
      autoApprove: t.autoApprove,
      noticeMode: t.advanceNoticeFromPolicy ? 'policy' : t.advanceNoticeDays !== null ? 'days' : 'none',
      noticeDays: t.advanceNoticeDays,
      deductsPay: t.deductsPay,
      adminOnly: t.adminOnly,
      countsCalendarDays: t.countsCalendarDays,
      sensitive: t.sensitive,
    };
    this.dialogError.set('');
    this.dialogOpen.set(true);
  }

  /** Where a type is charged and how it counts days are locked once it has leave; system types are fixed. */
  poolLocked(): boolean {
    return this.draft.hasLeave || this.draft.isSystem;
  }

  behaviourLocked(): boolean {
    return this.draft.isSystem;
  }

  /** What is wrong with the draft, in words (the server refuses the same things). */
  draftError(): string {
    const d = this.draft;
    if (d.editingKey === null) {
      const key = d.key.trim();
      if (key.length === 0 || key.length > 20 || !KEY_PATTERN.test(key))
        return 'The key is 1 to 20 lower-case letters, digits or hyphens (for example study-leave).';
      if (this.rows().some((r) => r.key === key)) return `A leave type with the key '${key}' already exists.`;
    }
    const label = d.label.trim();
    if (label.length === 0 || label.length > 40) return 'Give the type a name of up to 40 characters.';
    if (d.sortOrder === null || !Number.isFinite(d.sortOrder) || d.sortOrder < 0 || d.sortOrder > 1000)
      return 'The order must be between 0 and 1000.';
    if (d.pool === 'own' && !(d.allowanceDays !== null && d.allowanceDays > 0 && d.allowanceDays <= 366))
      return 'Enter the yearly allowance, between 0.5 and 366 days.';
    if (d.maxDaysPerRequest !== null && !(d.maxDaysPerRequest >= 0.5 && d.maxDaysPerRequest <= 366))
      return 'The longest single request must be between 0.5 and 366 days.';
    if (d.noticeMode === 'days' && !(d.noticeDays !== null && d.noticeDays >= 0 && d.noticeDays <= 60))
      return 'Notice must be between 0 and 60 days.';
    if (d.autoApprove && d.pool === 'none')
      return 'A type with no balance cannot approve itself: a long absence needs a manager.';
    if (d.autoApprove && d.adminOnly) return 'A type recorded by an administrator cannot also approve itself.';
    return '';
  }

  applyDraft(): void {
    const invalid = this.draftError();
    if (invalid) {
      this.dialogError.set(invalid);
      return;
    }
    const d = this.draft;
    const type: LeaveTypeDefinition = {
      key: d.editingKey ?? d.key.trim(),
      label: d.label.trim(),
      colour: d.colour,
      sortOrder: d.sortOrder ?? 0,
      active: d.isSystem ? true : d.active,
      isSystem: d.isSystem,
      pool: d.pool,
      allowanceDays: d.pool === 'own' ? d.allowanceDays : null,
      maxDaysPerRequest: d.maxDaysPerRequest,
      autoApprove: d.autoApprove,
      advanceNoticeDays: d.noticeMode === 'days' ? d.noticeDays : null,
      advanceNoticeFromPolicy: d.noticeMode === 'policy',
      deductsPay: d.deductsPay,
      adminOnly: d.adminOnly,
      countsCalendarDays: d.countsCalendarDays,
      sensitive: d.sensitive,
      hasLeave: d.hasLeave,
    };
    const rest = this.rows().filter((r) => r.key !== type.key);
    this.rows.set([...rest, type].sort((a, b) => a.sortOrder - b.sortOrder));
    this.dialogOpen.set(false);
  }

  cancelDialog(): void {
    this.dialogOpen.set(false);
  }

  // ── Turning on and off, removing ─────────────────────────────────────────────

  canToggle(t: LeaveTypeDefinition): boolean {
    return !t.isSystem;
  }

  toggleActive(t: LeaveTypeDefinition): void {
    if (!this.canToggle(t)) return;
    this.rows.update((rs) => rs.map((r) => (r.key === t.key ? { ...r, active: !r.active } : r)));
  }

  /** A type that has leave, or a system type, cannot be removed (deactivate it instead). */
  canRemove(t: LeaveTypeDefinition): boolean {
    return !t.isSystem && !t.hasLeave;
  }

  remove(t: LeaveTypeDefinition): void {
    if (!this.canRemove(t)) return;
    this.rows.update((rs) => rs.filter((r) => r.key !== t.key));
  }

  // ── Saving ───────────────────────────────────────────────────────────────────

  /** What is wrong with the catalogue as a whole, in words. */
  catalogueError(): string {
    const rows = this.rows();
    if (!rows.some((r) => r.active && !r.adminOnly)) return 'At least one active leave type must be open to staff.';
    return '';
  }

  save(): void {
    const invalid = this.catalogueError();
    if (invalid) {
      this.error.set(invalid);
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.conflict.set(false);
    this.typeSvc.save(this.version, this.rows()).subscribe({
      next: (version) => {
        this.version = version;
        this.savedJson = JSON.stringify(this.rows());
        this.toast.success('Leave types saved.');
        this.saving.set(false);
        // Pick up the usage flags again (a type may now be locked) without losing what was just saved.
        this.load();
      },
      error: (err) => {
        this.conflict.set(err?.status === 409);
        this.error.set(apiErrorMessage(err) ?? 'Could not save the leave types.');
        this.saving.set(false);
      },
    });
  }

  private blankDraft(): TypeDraft {
    return {
      editingKey: null,
      key: '',
      label: '',
      colour: 'info',
      sortOrder: 100,
      active: true,
      isSystem: false,
      hasLeave: false,
      pool: 'none',
      allowanceDays: null,
      maxDaysPerRequest: null,
      autoApprove: false,
      noticeMode: 'none',
      noticeDays: null,
      deductsPay: false,
      adminOnly: false,
      countsCalendarDays: false,
      sensitive: false,
    };
  }
}
