import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, of, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';

const PUBLIC_AUTH_PATHS = ['/api/Auth/login', '/api/Auth/refresh', '/api/Auth/logout'];

function isPublicAuthCall(req: HttpRequest<unknown>): boolean {
  return PUBLIC_AUTH_PATHS.some((p) => req.url.startsWith(`${environment.apiUrl}${p}`));
}

/**
 * Adds the bearer token to API calls. On a 401 it refreshes once (shared by all concurrent requests) and retries;
 * if the refresh itself fails the session is ended and the user is sent to /login.
 * Register this AFTER errorInterceptor so a recovered 401 never shows an error toast.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  if (!req.url.startsWith(environment.apiUrl) || isPublicAuthCall(req)) return next(req);

  const withToken = (token: string | null) => (token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req);
  const used = auth.accessToken();

  return next(withToken(used)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !auth.hasValidSession()) {
        return throwError(() => error);
      }

      // Another request may already have refreshed while this one was in flight: just retry with the newer token.
      const latest = auth.accessToken();
      const token$ = latest && latest !== used ? of(latest) : auth.refresh();

      return token$.pipe(
        catchError(() => {
          auth.endSession();
          return throwError(() => error);
        }),
        switchMap((token) => next(withToken(token))),
      );
    }),
  );
};
