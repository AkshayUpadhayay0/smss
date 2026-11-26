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
  // Parent / student account pages reached from the WhatsApp or email link. NO guard (a signed-in person may open them too) and never indexed (no seo.index).
  {
    path: 'parent/set-password',
    data: { seo: { title: 'Choose your password | SMSS', description: 'Choose your SMSS password.', index: false } },
    loadComponent: () => import('../parent-account/pages/set-password/set-password.component').then((m) => m.ParentSetPasswordComponent),
  },
  {
    path: 'parent/reset-password',
    data: { seo: { title: 'Choose a new password | SMSS', description: 'Choose a new SMSS password.', index: false } },
    loadComponent: () => import('../parent-account/pages/set-password/set-password.component').then((m) => m.ParentSetPasswordComponent),
  },
  {
    path: 'parent/forgot',
    data: { seo: { title: 'Forgot ID or password | SMSS', description: 'Recover your SMSS parent login.', index: false } },
    loadComponent: () => import('../parent-account/pages/forgot/forgot.component').then((m) => m.ParentForgotComponent),
  },
];
