import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/** Protects the main layout: signed-out users go to /login and come back to where they were headed. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.hasValidSession() ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Keeps signed-in users away from the login page. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.hasValidSession() ? router.createUrlTree(['/dashboard']) : true;
};

/**
 * The public landing page ("/"): a signed-in user is sent to the dashboard, everyone else sees the page.
 * Same rule as guestGuard, named for its purpose. During the build-time prerender there is no stored session, so the page renders.
 */
export const publicGuard: CanActivateFn = guestGuard;
