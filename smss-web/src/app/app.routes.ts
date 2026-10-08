import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

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
    loadComponent: () => import('./layouts/main-layout/main-layout.component').then((m) => m.MainLayoutComponent),
    children: [
      { path: '', loadChildren: () => import('./features/dashboard/dashboard.routes').then((m) => m.DASHBOARD_ROUTES) },
      { path: '', loadChildren: () => import('./features/schools/schools.routes').then((m) => m.SCHOOLS_ROUTES) },
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
