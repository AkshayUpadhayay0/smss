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
 * Also holds a first-login user (isFirstLogin) on /change-password until they have set their own password.
 */
const isChangePasswordUrl = (url: string) => /^\/change-password(?:[?#/]|$)/.test(url);

export const roleGuard: CanActivateChildFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const toast = inject(ToastService);

  if (!auth.isLoggedIn()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }
  // A temporary password (first login) must be replaced before ANY other page loads, whatever URL was typed.
  // Runs on every child navigation, so it also covers links clicked after landing on /change-password.
  if (auth.user()?.isFirstLogin && !isChangePasswordUrl(state.url)) {
    return router.createUrlTree(['/change-password']);
  }
  if (canAccess(state.url, auth.currentUserRoles())) return true;

  toast.danger("You don't have permission to view that page.", 'Access denied');
  return router.createUrlTree(['/dashboard']);
};
