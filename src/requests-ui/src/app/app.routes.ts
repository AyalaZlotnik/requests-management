import { Routes } from '@angular/router';

export const routes: Routes = [
  // Lazy: the page and its Material modules load as a separate chunk, keeping the initial bundle small.
  { path: '', loadComponent: () => import('./features/requests/requests-page.component').then((m) => m.RequestsPageComponent) },
  { path: '**', redirectTo: '' },
];
