import { Injectable, inject } from '@angular/core';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { filter, take } from 'rxjs/operators';
import { ToastService } from './toast.service';

@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  private readonly swUpdate = inject(SwUpdate);
  private readonly toast    = inject(ToastService);

  init(): void {
    if (!this.swUpdate.isEnabled) return;

    this.swUpdate.versionUpdates.pipe(
      filter((e): e is VersionReadyEvent => e.type === 'VERSION_READY'),
      take(1), // guard: if VERSION_READY fires multiple times, only trigger one reload
    ).subscribe(() => {
      this.toast.show('New version available — refreshing in 3 seconds...', 'info', 3500);
      setTimeout(() => {
        this.swUpdate.activateUpdate().then(() => window.location.reload());
      }, 3000);
    });
  }
}
