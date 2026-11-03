import { Routes } from '@angular/router';

// Mounted at the root: /employees, /employees/new, /employees/:employeeId and /employees/:employeeId/edit.
// 'new' is listed before ':employeeId' so it can't be swallowed by the parameter.
export const EMPLOYEES_ROUTES: Routes = [
  {
    // any employee's own read-only record (the API answers /api/Employee/me)
    path: 'my-profile',
    loadComponent: () => import('./pages/my-profile/my-profile.component').then((m) => m.MyProfileComponent),
  },
  {
    path: 'employees',
    loadComponent: () => import('./pages/employee-list/employee-list.component').then((m) => m.EmployeeListComponent),
  },
  {
    path: 'employees/new',
    loadComponent: () => import('./pages/employee-form/employee-form.component').then((m) => m.EmployeeFormComponent),
  },
  {
    // :employeeId is bound to the page's `employeeId` input (withComponentInputBinding)
    path: 'employees/:employeeId/edit',
    loadComponent: () => import('./pages/employee-form/employee-form.component').then((m) => m.EmployeeFormComponent),
  },
  {
    path: 'employees/:employeeId',
    loadComponent: () => import('./pages/employee-detail/employee-detail.component').then((m) => m.EmployeeDetailComponent),
  },
];
