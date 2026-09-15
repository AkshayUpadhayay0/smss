import { HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

/**
 * Centralized place to translate backend HTTP errors into user-facing
 * messages (e.g. via ToastService) once a real API is connected.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError((error) => {
      // eslint-disable-next-line no-console
      console.error('[HTTP ERROR]', req.url, error);
      return throwError(() => error);
    }),
  );
};
