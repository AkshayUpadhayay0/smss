import { Routes } from '@angular/router';

// Mounted at the root: /leave/my, /leave/requests and /leave/students.
export const LEAVE_ROUTES: Routes = [
  { path: 'leave/my', loadComponent: () => import('./pages/my-leave/my-leave.component').then((m) => m.MyLeaveComponent) },
  { path: 'leave/requests', loadComponent: () => import('./pages/leave-requests/leave-requests.component').then((m) => m.LeaveRequestsComponent) },
  { path: 'leave/students', loadComponent: () => import('./pages/student-leave/student-leave.component').then((m) => m.StudentLeaveComponent) },
];
