import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../services/toast.service';
import { ApiResponse } from '../models/api-response.model';

/** Set to true on a request (via HttpContext) when the caller shows its own error UI. */
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);

/** Flattens `{ Field: ["msg", …] }` into messages; anything else yields none. */
function collectMessages(value: unknown): string[] {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return [];
  return Object.values(value as Record<string, unknown>)
    .filter((v): v is string[] => Array.isArray(v) && v.every((m) => typeof m === 'string'))
    .flat();
}

/** Best human-readable message for a failed request: server `message`, else a status-based fallback. */
export function extractErrorMessage(error: HttpErrorResponse): string {
  const body = error.error as (Partial<ApiResponse<unknown>> & { errors?: Record<string, string[]> }) | string | null;
  if (body && typeof body === 'object') {
    // The API reports model-validation failures as message "Validation failed" + data: { Field: ["msg"] }.
    const fieldMessages = [...collectMessages(body.data), ...collectMessages(body.errors)];
    if (fieldMessages.length) return fieldMessages.slice(0, 3).join(' ');
    if (typeof body.message === 'string' && body.message) return body.message;
  }
  switch (error.status) {
    case 0:
      return 'Cannot reach the server. Check your connection and try again.';
    case 401:
      return 'Your session has expired. Please sign in again.';
    case 403:
      return 'You do not have permission to do that.';
    case 404:
      return 'The requested resource was not found.';
    default:
      return error.status >= 500 ? 'Something went wrong on the server. Please try again.' : 'The request could not be completed.';
  }
}

/** Shows a toast for failed API calls, then re-throws so callers can still react. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && !req.context.get(SKIP_ERROR_TOAST)) {
        toast.error(extractErrorMessage(error));
      }
      return throwError(() => error);
    }),
  );
};
