import { Routes } from '@angular/router';
import { guestGuard } from '../../core/guards/auth.guard';

/** Routes shown inside the auth layout (no sidebar). */
export const AUTH_ROUTES: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    // Not for search engines (also the default for any route without SEO data)
    data: { seo: { title: 'Sign in | SMSS', description: 'Sign in to SMSS.', index: false } },
    loadComponent: () => import('./login/login.component').then((m) => m.LoginComponent),
  },
];
