import { Routes } from '@angular/router';
import { guestGuard } from '../../core/guards/auth.guard';

/** Routes shown inside the auth layout (no sidebar). */
export const AUTH_ROUTES: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./login/login.component').then((m) => m.LoginComponent),
  },
];
