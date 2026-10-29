import { Routes } from '@angular/router';

// Mounted at the root. The expenses page takes `from`, `to`, `categoryId` and `status` as query parameters (component input binding).
export const COMPANY_FINANCE_ROUTES: Routes = [
  { path: 'company', pathMatch: 'full', redirectTo: 'company/overview' },
  {
    path: 'company/overview',
    loadComponent: () => import('./pages/overview-page/overview-page.component').then((m) => m.CompanyOverviewPageComponent),
  },
  {
    path: 'company/employees',
    loadComponent: () => import('./pages/employees-page/employees-page.component').then((m) => m.CompanyEmployeesPageComponent),
  },
  {
    path: 'company/expenses',
    loadComponent: () => import('./pages/expenses-page/expenses-page.component').then((m) => m.CompanyExpensesPageComponent),
  },
];
