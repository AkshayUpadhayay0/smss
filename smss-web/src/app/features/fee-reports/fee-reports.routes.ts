import { Routes } from '@angular/router';

// Mounted at the root: /fee-dashboard, /fee-reports/class and /fee-status (a class teacher's own section).
export const FEE_REPORTS_ROUTES: Routes = [
  {
    path: 'fee-dashboard',
    loadComponent: () => import('./pages/fee-dashboard/fee-dashboard.component').then((m) => m.FeeDashboardComponent),
  },
  {
    path: 'fee-status',
    loadComponent: () => import('./pages/section-fee-status/section-fee-status.component').then((m) => m.SectionFeeStatusComponent),
  },
  {
    path: 'fee-reports/class',
    loadComponent: () => import('./pages/class-fee-report/class-fee-report.component').then((m) => m.ClassFeeReportComponent),
  },
];
