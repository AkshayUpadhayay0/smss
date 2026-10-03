import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

const CHANGE_PASSWORD_URL = '/change-password';

export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return router.createUrlTree(['/login']);
  }

  // Temporary password issued at registration must be replaced before anything else
  if (authService.mustChangePassword() && !state.url.startsWith(CHANGE_PASSWORD_URL)) {
    return router.createUrlTree([CHANGE_PASSWORD_URL]);
  }

  return true;
};

/** Prevents an already-logged-in user from seeing Login/Register again. */
export const guestGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/dashboard']);
};
