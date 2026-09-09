import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Mock guard: this theme has no real backend, so the guard simply ensures the
 * demo "current user" exists in state before entering admin routes, auto-logging
 * the demo user in on first visit rather than blocking access to the template.
 */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) {
    auth.login('priya.sharma@greenvalley.edu.in', 'demo', 'Admin');
  }
  return true;
};
