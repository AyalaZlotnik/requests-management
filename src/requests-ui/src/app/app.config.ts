import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withFetch } from '@angular/common/http';
import localeHe from '@angular/common/locales/he';
import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { DateAdapter, MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { HebrewDateAdapter } from './core/i18n/hebrew-date-adapter';
import { HebrewPaginatorIntl } from './core/i18n/hebrew-paginator-intl';

registerLocaleData(localeHe);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch()),
    provideRouter(routes),
    { provide: LOCALE_ID, useValue: 'he' },
    { provide: MatPaginatorIntl, useClass: HebrewPaginatorIntl },
    // Date pickers: Hebrew calendar, day-first typing (see HebrewDateAdapter).
    provideNativeDateAdapter(),
    { provide: DateAdapter, useClass: HebrewDateAdapter },
    { provide: MAT_DATE_LOCALE, useValue: 'he-IL' },
  ],
};
