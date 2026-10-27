import { Routes } from '@angular/router';

export const DASHBOARD_ROUTES: Routes = [
  {
    path: 'dashboard',
    loadComponent: () => import('./pages/dashboard-page/dashboard-page.component').then((m) => m.DashboardPageComponent),
    data: {
      title: 'Dashboard',
      subtitle: 'Overview of your organization',
      icon: 'layout-dashboard',
      breadcrumbs: [{ label: 'Dashboard' }],
    },
  },
];
