export type ApiErrorKind =
  | 'network' // no response at all (offline, DNS, TLS, timeout)
  | 'unauthorized' // 401 after the one refresh attempt
  | 'password_change_required' // the first-login middleware's 403
  | 'forbidden' // any other 403
  | 'conflict' // 409
  | 'validation' // 400 / 422
  | 'not_found'
  | 'rate_limited'
  | 'server' // 5xx
  | 'unknown';

export const GENERIC_MESSAGE = 'Something went wrong. Please try again.';
export const NETWORK_MESSAGE = "Can't reach the server. Check your internet connection and try again.";

/** The first-login middleware answers with exactly this prefix (FirstLoginEnforcementMiddleware.Message). */
const PASSWORD_CHANGE_PREFIX = 'Password change required';

export class ApiError extends Error {
  constructor(
    public readonly kind: ApiErrorKind,
    /** Safe to show to the user: either the server's own message for a 4xx, or a generic one. Never a stack trace or raw body. */
    message: string,
    public readonly status: number | null = null,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/** Validation failures come back as { message: "Validation failed", data: { Field: ["msg", ...] } }. Prefer the first field message. */
function firstValidationMessage(data: unknown): string | null {
  if (!data || typeof data !== 'object') return null;
  for (const v of Object.values(data as Record<string, unknown>)) {
    if (Array.isArray(v) && typeof v[0] === 'string' && v[0].trim()) return v[0];
  }
  return null;
}

/**
 * Turns a failed HTTP response into an ApiError. The server's own message is used for 4xx (those are written for people);
 * a 5xx or an unreadable body gets the generic text. Nothing from the body is ever logged.
 */
export function toApiError(status: number, body: unknown): ApiError {
  const env = body && typeof body === 'object' ? (body as { message?: unknown; data?: unknown }) : {};
  const serverMessage = typeof env.message === 'string' && env.message.trim() ? env.message.trim() : null;

  if (status === 403 && serverMessage?.startsWith(PASSWORD_CHANGE_PREFIX)) {
    return new ApiError('password_change_required', serverMessage, status);
  }
  if (status >= 500) return new ApiError('server', GENERIC_MESSAGE, status);

  const safe = status === 400 || status === 422 ? (firstValidationMessage(env.data) ?? serverMessage) : serverMessage;
  switch (status) {
    case 400:
    case 422:
      return new ApiError('validation', safe ?? GENERIC_MESSAGE, status);
    case 401:
      return new ApiError('unauthorized', serverMessage ?? 'Your session has expired. Please sign in again.', status);
    case 403:
      return new ApiError('forbidden', serverMessage ?? "You don't have permission to do that.", status);
    case 404:
      return new ApiError('not_found', serverMessage ?? 'We could not find that.', status);
    case 409:
      return new ApiError('conflict', serverMessage ?? GENERIC_MESSAGE, status);
    case 429:
      return new ApiError('rate_limited', 'Too many attempts. Please wait a moment and try again.', status);
    default:
      return new ApiError('unknown', serverMessage ?? GENERIC_MESSAGE, status);
  }
}

/** Any thrown value -> a message that is safe to show. */
export function messageOf(error: unknown): string {
  return error instanceof ApiError ? error.message : GENERIC_MESSAGE;
}
