import { ApiError, GENERIC_MESSAGE, NETWORK_MESSAGE, toApiError } from './errors';
import type { AuthUser, LoginResponse } from './types';

/** What is kept between launches (in expo-secure-store; see src/auth/storage.ts). */
export interface StoredSession {
  accessToken: string;
  refreshToken: string;
  user: AuthUser;
}

export interface SessionStore {
  read(): Promise<StoredSession | null>;
  write(session: StoredSession): Promise<void>;
  clear(): Promise<void>;
}

export interface ClientHandlers {
  /** The session is over (refresh refused, or a 401 after a refresh). The store is already cleared. Navigate to sign-in. */
  onSessionExpired?: () => void;
  /** The server answered the first-login 403. The UI must send the user to Change Password and nowhere else. */
  onPasswordChangeRequired?: () => void;
  /** A new token pair was issued by a refresh; carries the up-to-date user. */
  onSession?: (user: AuthUser) => void;
}

export interface ClientDeps {
  baseUrl: () => string;
  store: SessionStore;
  fetchFn?: typeof fetch;
  timeoutMs?: number;
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  query?: Record<string, string | number | boolean | undefined | null>;
  body?: unknown;
  /** false for anonymous calls (login, refresh, logout). Default true. */
  auth?: boolean;
  signal?: AbortSignal;
}

const REFRESH_PATH = '/api/Auth/refresh';
const EXPIRED = 'Your session has expired. Please sign in again.';

function toQuery(query: RequestOptions['query']): string {
  if (!query) return '';
  const parts: string[] = [];
  for (const [k, v] of Object.entries(query)) {
    if (v === undefined || v === null || v === '') continue;
    parts.push(`${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`);
  }
  return parts.length ? `?${parts.join('&')}` : '';
}

function fromLogin(login: LoginResponse): StoredSession {
  return { accessToken: login.token, refreshToken: login.refreshToken, user: login.user };
}

/**
 * The one HTTP layer. Wraps the {status, statusCode, message, data} envelope and returns `data`.
 *
 * Refresh rules (the server rotates refresh tokens: each works once, and reusing an old one revokes every session):
 *  - Any number of concurrent 401s trigger exactly ONE refresh; the others wait for it and retry with the new token.
 *  - The new pair is written to secure storage BEFORE the refresh promise resolves (so nothing else can use or lose it).
 *  - A refresh that fails with no response at all is retried ONCE with the same token. That recovers a request that never reached the server. If the
 *    server DID rotate the token and only the reply was lost, the retry gets 401 (verified live: a replayed token inside the 10 s grace period is refused,
 *    not re-issued; the grace period only stops the replay from revoking every session), so the user is signed in again. There is no way around that client-side.
 *  - A 401/403 from refresh ends the session. A 5xx/network failure does not: the session is kept and the user can retry.
 * Nothing here logs a token, a password or a response body.
 */
export function createApiClient(deps: ClientDeps) {
  const fetchFn = deps.fetchFn ?? ((...args: Parameters<typeof fetch>) => fetch(...args));
  const timeoutMs = deps.timeoutMs ?? 20_000;
  let handlers: ClientHandlers = {};

  let mem: StoredSession | null | undefined; // undefined = not read from the store yet
  let refreshing: Promise<StoredSession> | null = null;
  let epoch = 0; // bumped whenever the session is replaced or ended, so a late refresh can't resurrect a signed-out session

  async function current(): Promise<StoredSession | null> {
    if (mem === undefined) mem = await deps.store.read();
    return mem;
  }

  async function endSession(notify: boolean): Promise<void> {
    epoch++;
    mem = null;
    refreshing = null;
    try {
      await deps.store.clear();
    } catch {
      // the in-memory session is gone either way
    }
    if (notify) handlers.onSessionExpired?.();
  }

  async function send(path: string, opts: RequestOptions, accessToken: string | null): Promise<Response> {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    const onAbort = () => controller.abort();
    opts.signal?.addEventListener('abort', onAbort);
    try {
      const headers: Record<string, string> = { Accept: 'application/json' };
      if (opts.body !== undefined) headers['Content-Type'] = 'application/json';
      if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
      return await fetchFn(`${deps.baseUrl()}${path}${toQuery(opts.query)}`, {
        method: opts.method ?? 'GET',
        headers,
        body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
        signal: controller.signal,
      });
    } catch {
      throw new ApiError('network', NETWORK_MESSAGE);
    } finally {
      clearTimeout(timer);
      opts.signal?.removeEventListener('abort', onAbort);
    }
  }

  async function readBody(res: Response): Promise<unknown> {
    try {
      const text = await res.text();
      return text ? JSON.parse(text) : null;
    } catch {
      return null;
    }
  }

  async function parse<T>(res: Response): Promise<T> {
    const body = await readBody(res);
    if (res.ok) {
      if (!body || typeof body !== 'object' || !('data' in body)) throw new ApiError('unknown', GENERIC_MESSAGE, res.status);
      return (body as { data: T }).data;
    }
    const error = toApiError(res.status, body);
    if (error.kind === 'password_change_required') handlers.onPasswordChangeRequired?.();
    throw error;
  }

  async function doRefresh(stale: StoredSession): Promise<StoredSession> {
    const startEpoch = epoch;
    const call = () => send(REFRESH_PATH, { method: 'POST', body: { refreshToken: stale.refreshToken }, auth: false }, null);

    let res: Response;
    try {
      res = await call();
    } catch (e) {
      if (!(e instanceof ApiError) || e.kind !== 'network') throw e;
      res = await call(); // ONE retry, same token
    }

    if (res.status === 401 || res.status === 403) {
      if (epoch === startEpoch) await endSession(true);
      throw new ApiError('unauthorized', EXPIRED, res.status);
    }
    const login = await parse<LoginResponse>(res); // other failures (5xx, 429) throw and leave the session alone
    if (epoch !== startEpoch) throw new ApiError('unauthorized', EXPIRED, 401);

    const next = fromLogin(login);
    mem = next;
    epoch++; // synchronously with the assignment above, so a concurrent sign-out always sees a newer epoch
    try {
      await deps.store.write(next); // persisted BEFORE the lock is released
    } catch {
      // keep the in-memory pair; the app still works this run
    }
    handlers.onSession?.(next.user);
    return next;
  }

  /** Single-flight. `usedToken` is the access token the failed request carried: if the session already moved on, just use the new one. */
  async function refreshOnce(usedToken: string): Promise<StoredSession> {
    const sess = await current();
    if (!sess) throw new ApiError('unauthorized', EXPIRED, 401);
    if (sess.accessToken !== usedToken) return sess;
    if (!refreshing) {
      const p: Promise<StoredSession> = doRefresh(sess).finally(() => {
        if (refreshing === p) refreshing = null;
      });
      refreshing = p;
    }
    return refreshing;
  }

  /** Sends an authenticated request, refreshing once on a 401. Returns the raw Response (the caller parses it). */
  async function authedSend(path: string, opts: RequestOptions): Promise<Response> {
    const sess = await current();
    if (!sess) throw new ApiError('unauthorized', 'Please sign in.', 401);

    let res = await send(path, opts, sess.accessToken);
    if (res.status === 401) {
      const fresh = await refreshOnce(sess.accessToken);
      res = await send(path, opts, fresh.accessToken);
      if (res.status === 401) {
        await endSession(true);
        throw new ApiError('unauthorized', EXPIRED, 401);
      }
    }
    return res;
  }

  async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
    if (opts.auth === false) return parse<T>(await send(path, opts, null));
    return parse<T>(await authedSend(path, opts));
  }

  /** An authenticated file download (e.g. a homework attachment). A failure answers with the JSON envelope, which becomes an ApiError like any other. */
  async function download(path: string, opts: Pick<RequestOptions, 'signal'> = {}): Promise<{ bytes: ArrayBuffer; contentType: string }> {
    const res = await authedSend(path, opts);
    if (!res.ok) {
      const error = toApiError(res.status, await readBody(res));
      if (error.kind === 'password_change_required') handlers.onPasswordChangeRequired?.();
      throw error;
    }
    return { bytes: await res.arrayBuffer(), contentType: res.headers.get('content-type') ?? 'application/octet-stream' };
  }

  return {
    request,
    download,
    setHandlers(next: ClientHandlers) {
      handlers = next;
    },
    /** The stored session, for app start-up. */
    loadSession: current,
    /** After login / change-password: replace the session with the new pair. */
    async startSession(login: LoginResponse): Promise<void> {
      epoch++;
      mem = fromLogin(login);
      await deps.store.write(mem);
    },
    /** Sign-out / failed start-up: forget everything without calling onSessionExpired. */
    clearSession: () => endSession(false),
    /** The refresh token, for POST /Auth/logout. */
    async refreshToken(): Promise<string | null> {
      return (await current())?.refreshToken ?? null;
    },
  };
}

export type ApiClient = ReturnType<typeof createApiClient>;
