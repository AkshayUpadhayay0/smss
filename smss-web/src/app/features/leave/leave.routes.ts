import { Routes } from '@angular/router';

// Mounted at the root: /leave/my, /leave/requests and /leave/students.
export const LEAVE_ROUTES: Routes = [
  { path: 'leave/my', loadComponent: () => import('./pages/my-leave/my-leave.component').then((m) => m.MyLeaveComponent) },
  // A Student's own leave: requests, a new request, cancel while Pending (the same component is the Leave tab of the student's My Profile)
  { path: 'leave/mine', loadComponent: () => import('./pages/student-my-leave/student-my-leave-page.component').then((m) => m.StudentMyLeavePageComponent) },
  { path: 'leave/requests', loadComponent: () => import('./pages/leave-requests/leave-requests.component').then((m) => m.LeaveRequestsComponent) },
  { path: 'leave/students', loadComponent: () => import('./pages/student-leave/student-leave.component').then((m) => m.StudentLeaveComponent) },
];
