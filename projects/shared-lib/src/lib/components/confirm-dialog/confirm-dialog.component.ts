import { Component } from '@angular/core';
import { ConfirmDialog } from 'primeng/confirmdialog';

@Component({
  selector: 'nc-confirm-dialog',
  standalone: true,
  imports: [ConfirmDialog],
  template: `<p-confirmDialog [style]="{ width: 'min(440px, 92vw)' }" />`,
})
export class ConfirmDialogComponent {}
