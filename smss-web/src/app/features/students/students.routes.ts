import { Routes } from '@angular/router';

// Mounted at the root: /students and /students/:studentId.
// IMPORTANT: this file must be mounted AFTER student-onboarding.routes in app.routes.ts, so that
// /students/onboard-existing is matched before ':studentId' can swallow it.
export const STUDENTS_ROUTES: Routes = [
  // A Teacher's own students (class-teacher sections + sections they teach in). The profile is the SAME component in restricted mode (route data).
  {
    path: 'my-students',
    loadComponent: () => import('./pages/my-students/my-students.component').then((m) => m.MyStudentsComponent),
  },
  {
    path: 'my-students/:studentId',
    data: { restricted: true },
    loadComponent: () => import('./pages/student-profile/student-profile.component').then((m) => m.StudentProfileComponent),
  },
  {
    path: 'students',
    loadComponent: () => import('./pages/student-list/student-list.component').then((m) => m.StudentListComponent),
  },
  {
    path: 'students/:studentId',
    loadComponent: () => import('./pages/student-profile/student-profile.component').then((m) => m.StudentProfileComponent),
  },
];
