import { Routes } from '@angular/router';

// Mounted at the root: /student-leaving and /section-change. School Admin and Principal open them (ROUTE_ROLES); the API re-checks.
export const STUDENT_LEAVING_ROUTES: Routes = [
  { path: 'student-leaving', loadComponent: () => import('./pages/leaving/leaving.component').then((m) => m.LeavingComponent) },
  { path: 'section-change', loadComponent: () => import('./pages/section-change/section-change.component').then((m) => m.SectionChangeComponent) },
];
