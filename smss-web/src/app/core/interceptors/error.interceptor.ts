import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

const AUTH_ENDPOINTS = ['/api/Auth/login', '/api/Auth/refresh', '/api/Auth/logout'];

/**
 * Centralized place to translate backend HTTP errors. A 401 on one of our own API calls means the
 * short-lived access token expired: refresh it once and replay the request. If the refresh itself
 * fails the session is over, so it is cleared and the user is sent back to the login page.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const isOwnApi = req.url.startsWith(environment.apiUrl);
  const isAuthEndpoint = AUTH_ENDPOINTS.some((path) => req.url.includes(path));

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && isOwnApi && !isAuthEndpoint && authService.isAuthenticated()) {
        return authService.refreshSession().pipe(
          switchMap((session) =>
            next(req.clone({ setHeaders: { Authorization: `Bearer ${session.token}` } })),
          ),
          catchError((refreshError: HttpErrorResponse) => {
            // Replayed request failing for another reason is not a dead session; a failed refresh is
            if (refreshError.url?.includes('/api/Auth/refresh') || !(refreshError instanceof HttpErrorResponse)) {
              authService.logout();
              router.navigate(['/login']);
            }
            return throwError(() => refreshError);
          }),
        );
      }

      // eslint-disable-next-line no-console
      console.error('[HTTP ERROR]', req.url, error);
      return throwError(() => error);
    }),
  );
};
