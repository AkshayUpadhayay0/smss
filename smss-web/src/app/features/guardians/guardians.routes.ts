import { Routes } from '@angular/router';

// Mounted at the root: /parent-details (the "Complete parent details" list). School Admin, Principal and Admission Staff open it (ROUTE_ROLES).
export const GUARDIANS_ROUTES: Routes = [
  {
    path: 'parent-details',
    loadComponent: () => import('./pages/parent-details/parent-details.component').then((m) => m.ParentDetailsComponent),
  },
];
