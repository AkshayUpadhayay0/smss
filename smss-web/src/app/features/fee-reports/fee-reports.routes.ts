import { Routes } from '@angular/router';

// Mounted at the root: /fee-dashboard and /fee-reports/class.
export const FEE_REPORTS_ROUTES: Routes = [
  {
    path: 'fee-dashboard',
    loadComponent: () => import('./pages/fee-dashboard/fee-dashboard.component').then((m) => m.FeeDashboardComponent),
  },
  {
    path: 'fee-reports/class',
    loadComponent: () => import('./pages/class-fee-report/class-fee-report.component').then((m) => m.ClassFeeReportComponent),
  },
];
