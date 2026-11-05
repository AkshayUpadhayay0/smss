import { Routes } from '@angular/router';

// Mounted at the root: /attendance/mark and /attendance/report.
export const ATTENDANCE_ROUTES: Routes = [
  {
    path: 'attendance/mark',
    loadComponent: () => import('./pages/mark-attendance/mark-attendance.component').then((m) => m.MarkAttendanceComponent),
  },
  {
    path: 'attendance/report',
    loadComponent: () => import('./pages/attendance-report/attendance-report.component').then((m) => m.AttendanceReportComponent),
  },
];
