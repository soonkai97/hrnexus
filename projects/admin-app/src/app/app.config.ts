import { ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners, provideAppInitializer, inject } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors, HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { provideServiceWorker } from '@angular/service-worker';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { providePrimeNG } from 'primeng/config';
import { MessageService, ConfirmationService } from 'primeng/api';
import Aura from '@primeuix/themes/aura';
import { definePreset } from '@primeuix/themes';

const HRNexusTheme = definePreset(Aura, {
  semantic: {
    primary: {
      50:  '{blue.50}',
      100: '{blue.100}',
      200: '{blue.200}',
      300: '{blue.300}',
      400: '{blue.400}',
      500: '{blue.500}',
      600: '{blue.600}',
      700: '{blue.700}',
      800: '{blue.800}',
      900: '{blue.900}',
      950: '{blue.950}',
    },
  },
});

import { routes } from './app.routes';
import { jwtInterceptor, maintenanceInterceptor, provideNoAutofill } from 'shared-lib';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([maintenanceInterceptor, jwtInterceptor])),
    provideAppInitializer(async () => {
      // Check maintenance status before the app renders anything.
      // /api/health is NOT in any SW dataGroup so it always hits nginx directly,
      // bypassing the SW cache — even if the phone has cached API responses.
      // On 503: maintenanceInterceptor redirects to /maintenance.html and the
      //         promise blocks indefinitely so no cached app flashes.
      // On network error: catch swallows it and the app boots from cache (offline).
      const http = inject(HttpClient);
      try {
        await firstValueFrom(http.get('/api/health', { responseType: 'text' }));
      } catch (err: unknown) {
        if ((err as { status?: number })?.status === 503) {
          await new Promise<never>(() => { /* block bootstrap — page is navigating to /maintenance.html */ });
        }
      }
    }),
    provideNoAutofill(),
    provideTranslateService({ defaultLanguage: 'en' }),
    ...provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
    providePrimeNG({
      theme: {
        preset: HRNexusTheme,
        options: { darkModeSelector: '[data-theme="dark"]' },
      },
    }),
    MessageService,
    ConfirmationService,
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
