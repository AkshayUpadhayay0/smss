import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

/** Attaches the JWT to requests that go to our own API (never to third-party URLs). */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).session()?.token;

  if (!token || !req.url.startsWith(environment.apiUrl)) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
