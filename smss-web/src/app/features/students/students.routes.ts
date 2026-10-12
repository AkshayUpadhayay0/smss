import { Routes } from '@angular/router';

// Mounted at the root: /students and /students/:studentId.
// IMPORTANT: this file must be mounted AFTER student-onboarding.routes in app.routes.ts, so that
// /students/onboard-existing is matched before ':studentId' can swallow it.
export const STUDENTS_ROUTES: Routes = [
  {
    path: 'students',
    loadComponent: () => import('./pages/student-list/student-list.component').then((m) => m.StudentListComponent),
  },
  {
    path: 'students/:studentId',
    loadComponent: () => import('./pages/student-profile/student-profile.component').then((m) => m.StudentProfileComponent),
  },
];
