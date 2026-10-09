import { inject } from '@angular/core';
import { CanActivateChildFn, Router } from '@angular/router';
import { canAccess } from '../config/route-roles';
import { AuthService } from '../services/auth.service';
import { ToastService } from '../services/toast.service';

/**
 * Role check on top of "is signed in", driven by ROUTE_ROLES (the same config the sidebar uses).
 * Registered as canActivateChild on the main layout so it re-runs for EVERY navigation between child pages
 * (a plain canActivate on the layout would only run once, when the layout is first entered).
 * Fails closed: a URL with no ROUTE_ROLES entry is denied.
 */
export const roleGuard: CanActivateChildFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const toast = inject(ToastService);

  if (!auth.isLoggedIn()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }
  if (canAccess(state.url, auth.currentUserRoles())) return true;

  toast.danger("You don't have permission to view that page.", 'Access denied');
  return router.createUrlTree(['/dashboard']);
};
