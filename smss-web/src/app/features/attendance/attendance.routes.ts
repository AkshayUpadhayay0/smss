import { Routes } from '@angular/router';

// Mounted at the root: /attendance/mark.
export const ATTENDANCE_ROUTES: Routes = [
  {
    path: 'attendance/mark',
    loadComponent: () => import('./pages/mark-attendance/mark-attendance.component').then((m) => m.MarkAttendanceComponent),
  },
];
