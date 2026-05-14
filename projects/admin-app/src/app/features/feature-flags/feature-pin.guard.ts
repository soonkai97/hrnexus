import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { FeaturePinService } from './feature-pin.service';

export const featurePinGuard: CanActivateFn = () => {
  const pinSvc = inject(FeaturePinService);
  const router = inject(Router);
  return pinSvc.isUnlocked() ? true : router.createUrlTree(['/features/unlock']);
};
