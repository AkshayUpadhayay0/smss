import { Routes } from '@angular/router';

// Mounted at the root. Query parameters (`status`, `schoolId`, `schoolName`) reach the pages as inputs (component input binding).
export const SUBSCRIPTIONS_ROUTES: Routes = [
  {
    path: 'subscriptions',
    loadComponent: () => import('./pages/subscriptions-page/subscriptions-page.component').then((m) => m.SubscriptionsPageComponent),
  },
  {
    path: 'invoices',
    loadComponent: () => import('./pages/invoices-page/invoices-page.component').then((m) => m.InvoicesPageComponent),
  },
];
