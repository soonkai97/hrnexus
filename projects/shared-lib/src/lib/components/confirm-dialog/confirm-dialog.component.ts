import { Component, ChangeDetectionStrategy } from '@angular/core';
import { ConfirmDialog } from 'primeng/confirmdialog';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'nc-confirm-dialog',
  standalone: true,
  imports: [ConfirmDialog],
  template: `<p-confirm-dialog [style]="{ width: 'min(440px, 92vw)' }" />`,
})
export class ConfirmDialogComponent {}
