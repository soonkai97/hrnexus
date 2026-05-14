import { Component, inject } from '@angular/core';
import { ConfirmService } from '../../services/confirm.service';

@Component({
  selector: 'nc-confirm-dialog',
  standalone: true,
  imports: [],
  templateUrl: './confirm-dialog.component.html',
  styleUrl:    './confirm-dialog.component.scss',
})
export class ConfirmDialogComponent {
  readonly svc = inject(ConfirmService);
}
