import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { roleGuard } from './core/guards/role.guard';

// Feature route files are mounted at the ROOT path (e.g. /schools, /masters/board-type).
export const routes: Routes = [
  // Must come first: otherwise the empty-path auth layout below matches "/" and renders an empty card.
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },

  // Auth layout branch: centered card, no sidebar
  {
    path: '',
    loadComponent: () => import('./layouts/auth-layout/auth-layout.component').then((m) => m.AuthLayoutComponent),
    loadChildren: () => import('./features/auth/auth.routes').then((m) => m.AUTH_ROUTES),
  },

  // Main layout branch: sidebar + topbar. Everything inside requires a signed-in user.
  {
    path: '',
    canActivate: [authGuard],
    canActivateChild: [roleGuard], // per-page role check, re-run on every child navigation
    loadComponent: () => import('./layouts/main-layout/main-layout.component').then((m) => m.MainLayoutComponent),
    children: [
      { path: '', loadChildren: () => import('./features/dashboard/dashboard.routes').then((m) => m.DASHBOARD_ROUTES) },
      { path: '', loadChildren: () => import('./features/schools/schools.routes').then((m) => m.SCHOOLS_ROUTES) },
      { path: '', loadChildren: () => import('./features/school-profile/school-profile.routes').then((m) => m.SCHOOL_PROFILE_ROUTES) },
      { path: '', loadChildren: () => import('./features/admissions/admissions.routes').then((m) => m.ADMISSIONS_ROUTES) },
      { path: '', loadChildren: () => import('./features/student-onboarding/student-onboarding.routes').then((m) => m.STUDENT_ONBOARDING_ROUTES) },
      // after student-onboarding: '/students/onboard-existing' must match before '/students/:studentId'
      { path: '', loadChildren: () => import('./features/students/students.routes').then((m) => m.STUDENTS_ROUTES) },
      { path: '', loadChildren: () => import('./features/school-setup/school-setup.routes').then((m) => m.SCHOOL_SETUP_ROUTES) },
      { path: '', loadChildren: () => import('./features/fee-setup/fee-setup.routes').then((m) => m.FEE_SETUP_ROUTES) },
      { path: '', loadChildren: () => import('./features/fee-reports/fee-reports.routes').then((m) => m.FEE_REPORTS_ROUTES) },
      { path: '', loadChildren: () => import('./features/employees/employees.routes').then((m) => m.EMPLOYEES_ROUTES) },
      { path: '', loadChildren: () => import('./features/attendance/attendance.routes').then((m) => m.ATTENDANCE_ROUTES) },
      { path: '', loadChildren: () => import('./features/calendar/calendar.routes').then((m) => m.CALENDAR_ROUTES) },
      { path: '', loadChildren: () => import('./features/homework/homework.routes').then((m) => m.HOMEWORK_ROUTES) },
      { path: '', loadChildren: () => import('./features/leave/leave.routes').then((m) => m.LEAVE_ROUTES) },
      { path: '', loadChildren: () => import('./features/timetable/timetable.routes').then((m) => m.TIMETABLE_ROUTES) },
      { path: '', loadChildren: () => import('./features/masters/masters.routes').then((m) => m.MASTERS_ROUTES) },
      {
        path: 'change-password',
        loadComponent: () => import('./features/auth/change-password/change-password.component').then((m) => m.ChangePasswordComponent),
      },
      // Throwaway: remove once the look & feel is approved.
      {
        path: 'dev/components-preview',
        loadComponent: () => import('./dev/components-preview/components-preview.component').then((m) => m.ComponentsPreviewComponent),
      },
    ],
  },

  { path: '**', redirectTo: 'dashboard' },
];
