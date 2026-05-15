import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { providePrimeNG } from 'primeng/config';
import { MessageService, ConfirmationService } from 'primeng/api';
import Aura from '@primeuix/themes/aura';
import { definePreset } from '@primeuix/themes';

const NightCrewTheme = definePreset(Aura, {
  semantic: {
    primary: {
      50:  '{violet.50}',
      100: '{violet.100}',
      200: '{violet.200}',
      300: '{violet.300}',
      400: '{violet.400}',
      500: '{violet.500}',
      600: '{violet.600}',
      700: '{violet.700}',
      800: '{violet.800}',
      900: '{violet.900}',
      950: '{violet.950}',
    },
  },
});

import { routes } from './app.routes';
import { jwtInterceptor, provideNoAutofill } from 'shared-lib';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([jwtInterceptor])),
    provideNoAutofill(),
    provideTranslateService({ defaultLanguage: 'en' }),
    ...provideTranslateHttpLoader({ prefix: '/i18n/', suffix: `.json?v=${Date.now()}` }),
    providePrimeNG({
      theme: {
        preset: NightCrewTheme,
        options: { darkModeSelector: '[data-theme="dark"]' },
      },
    }),
    MessageService,
    ConfirmationService,
  ],
};
