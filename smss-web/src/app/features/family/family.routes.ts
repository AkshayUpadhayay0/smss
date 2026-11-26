import { Routes } from '@angular/router';

/**
 * The parent's screens, mounted at /family inside the parent shell (no staff sidebar). Every URL here is in ROUTE_ROLES for the Guardian role only; the roleGuard fails closed for anything else
 * (a Student or a staff login that opens /family is sent away, and a Guardian that opens a staff URL is sent back to /family).
 */
export const FAMILY_ROUTES: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./pages/family-home/family-home.component').then((m) => m.FamilyHomeComponent) },
  { path: 'children/:studentId', loadComponent: () => import('./pages/child-dashboard/child-dashboard.component').then((m) => m.ChildDashboardComponent) },
  { path: 'notices', loadComponent: () => import('./pages/family-pages').then((m) => m.FamilyNoticesComponent) },
  { path: 'calendar', loadComponent: () => import('./pages/family-pages').then((m) => m.FamilyCalendarComponent) },
  { path: 'account', pathMatch: 'full', loadComponent: () => import('./pages/family-pages').then((m) => m.FamilyAccountComponent) },
  { path: 'account/change-password', loadComponent: () => import('../auth/change-password/change-password.component').then((m) => m.ChangePasswordComponent) },
];
