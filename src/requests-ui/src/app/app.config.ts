import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withFetch } from '@angular/common/http';
import localeHe from '@angular/common/locales/he';
import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { HebrewPaginatorIntl } from './core/i18n/hebrew-paginator-intl';

registerLocaleData(localeHe);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch()),
    provideRouter(routes),
    { provide: LOCALE_ID, useValue: 'he' },
    { provide: MatPaginatorIntl, useClass: HebrewPaginatorIntl },
  ],
};
