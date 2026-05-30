import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs/operators';
import { FeatureFlagService, FeatureKey } from '../services/feature-flag.service';

/**
 * Route guard that blocks access to a route when a feature flag is disabled.
 *
 * Usage:
 *   { path: 'announcements', canActivate: [featureFlagGuard('announcements', '/home')], ... }
 *
 * Important: Angular resolves ALL guards before instantiating ANY component, so the
 * ShellComponent constructor (which normally calls featureSvc.load()) has NOT run yet
 * on a fresh page load. The guard therefore triggers load() itself when flags are not
 * ready — the shell's subsequent load() call is a harmless duplicate GET.
 */
export const featureFlagGuard = (key: FeatureKey, fallback = '/'): CanActivateFn => () => {
  const featureSvc = inject(FeatureFlagService);
  const router     = inject(Router);

  const decide = () =>
    featureSvc.isEnabled(key) ? true : router.createUrlTree([fallback]);

  // Flags already loaded (in-app navigation) — answer synchronously
  if (featureSvc.loaded()) return decide();

  // Fresh page load: shell hasn't initialised yet, so trigger the load here.
  // featureSvc.load() sets loaded=true on both success and error.
  return featureSvc.load().pipe(map(decide));
};
