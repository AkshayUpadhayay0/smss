import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },

  {
    path: '',
    loadComponent: () => import('./layouts/auth-layout/auth-layout.component').then((m) => m.AuthLayoutComponent),
    canActivate: [guestGuard],
    children: [
      {
        path: 'login',
        loadComponent: () => import('./features/auth/login/login.component').then((m) => m.LoginPageComponent),
        title: 'Sign In — smss-web',
      },
      {
        path: 'register',
        loadComponent: () =>
          import('./features/auth/register/register.component').then((m) => m.RegisterPageComponent),
        title: 'Create Organization — smss-web',
      },
    ],
  },

  {
    path: '',
    loadComponent: () => import('./layouts/main-layout/main-layout.component').then((m) => m.MainLayoutComponent),
    canActivate: [authGuard],
    children: [
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
        title: 'Dashboard — smss-web',
        data: { breadcrumb: 'Dashboard' },
      },
      {
        path: 'table',
        loadComponent: () => import('./features/table/table.component').then((m) => m.TablePageComponent),
        title: 'Table — smss-web',
        data: { breadcrumb: ['Management', 'Table'] },
      },
      {
        path: 'form',
        loadComponent: () => import('./features/forms/forms.component').then((m) => m.FormsPageComponent),
        title: 'Form — smss-web',
        data: { breadcrumb: ['Management', 'Form'] },
      },
      {
        path: 'ui-elements',
        loadComponent: () =>
          import('./features/ui-elements/ui-elements.component').then((m) => m.UiElementsPageComponent),
        title: 'UI Elements — smss-web',
        data: { breadcrumb: ['Management', 'UI Elements'] },
      },
      {
        path: 'modals',
        loadComponent: () => import('./features/modals/modals.component').then((m) => m.ModalsPageComponent),
        title: 'Modals — smss-web',
        data: { breadcrumb: ['Management', 'Modals'] },
      },
      {
        path: 'swagger',
        loadComponent: () => import('./features/swagger/swagger.component').then((m) => m.SwaggerPageComponent),
        title: 'Swagger — smss-web',
        data: { breadcrumb: ['Documentation', 'Swagger'] },
      },
      {
        path: 'example/a',
        loadComponent: () =>
          import('./features/examples/example-a/example-a.component').then((m) => m.ExampleAComponent),
        title: 'Example A — smss-web',
        data: { breadcrumb: ['Examples', 'A'] },
      },
      {
        path: 'example/a/b',
        loadComponent: () =>
          import('./features/examples/example-b/example-b.component').then((m) => m.ExampleBComponent),
        title: 'Example B — smss-web',
        data: { breadcrumb: ['Examples', 'A', 'B'] },
      },
      {
        path: 'example/a/b/c',
        loadComponent: () =>
          import('./features/examples/example-c/example-c.component').then((m) => m.ExampleCComponent),
        title: 'Example C — smss-web',
        data: { breadcrumb: ['Examples', 'A', 'B', 'C'] },
      },
      {
        path: 'example/a/b/c/d',
        loadComponent: () =>
          import('./features/examples/example-d/example-d.component').then((m) => m.ExampleDComponent),
        title: 'Example D — smss-web',
        data: { breadcrumb: ['Examples', 'A', 'B', 'C', 'D'] },
      },
    ],
  },

  { path: '**', redirectTo: 'dashboard' },
];
