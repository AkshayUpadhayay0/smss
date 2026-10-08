import { Routes } from '@angular/router';

export const DASHBOARD_ROUTES: Routes = [
  {
    path: 'dashboard',
    loadComponent: () => import('../placeholder/placeholder-page.component').then((m) => m.PlaceholderPageComponent),
    data: {
      title: 'Dashboard',
      subtitle: 'Overview of your organization',
      icon: 'layout-dashboard',
      breadcrumbs: [{ label: 'Dashboard' }],
    },
  },
];
