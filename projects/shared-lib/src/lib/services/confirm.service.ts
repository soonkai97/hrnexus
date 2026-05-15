import { Injectable, inject } from '@angular/core';
import { Subject } from 'rxjs';
import { ConfirmationService } from 'primeng/api';

export interface ConfirmOptions {
  title?: string;
  message: string;
  confirmLabel?: string;
  danger?: boolean;
}

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly primeSvc = inject(ConfirmationService);

  confirm(opts: ConfirmOptions) {
    const result$ = new Subject<boolean>();
    this.primeSvc.confirm({
      header:         opts.title ?? 'Confirm',
      message:        opts.message,
      acceptLabel:    opts.confirmLabel ?? 'Confirm',
      rejectLabel:    'Cancel',
      acceptButtonProps: { severity: opts.danger ? 'danger' : 'primary' },
      accept:  () => { result$.next(true);  result$.complete(); },
      reject:  () => { result$.next(false); result$.complete(); },
    });
    return result$.asObservable();
  }
}
