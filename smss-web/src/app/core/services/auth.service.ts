import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, finalize, map, of, shareReplay, tap, throwError, catchError } from 'rxjs';
import { AuthSession, AuthUser, ChangePasswordRequest, LoginRequest, LoginResponse } from '../models/auth.model';
import { ApiService } from './api.service';

const STORAGE_KEY = 'smss.session';
const BASE = '/api/Auth';

/**
 * JWT session handling. Refresh tokens are single-use on the server (rotated on every refresh, and replaying
 * an old one revokes every session of that user), so `refresh()` is single-flight: concurrent callers share one request.
 *
 * Storage: "Remember me" -> localStorage, otherwise sessionStorage. (Tokens in web storage are readable by any
 * script running on the page, the usual SPA trade-off — keep third-party scripts out of this app.)
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  private readonly session = signal<AuthSession | null>(this.load());
  private refreshing$: Observable<string> | null = null;

  readonly user = computed<AuthUser | null>(() => this.session()?.user ?? null);

  /**
   * Role NAMES of the signed-in user (from login/refresh, restored synchronously from storage, so the first render
   * already has them — no flash of the wrong menu). Empty when signed out.
   */
  readonly currentUserRoles = computed<readonly string[]>(() => this.session()?.user.roles ?? []);

  isLoggedIn(): boolean {
    return this.hasValidSession();
  }

  accessToken(): string | null {
    return this.session()?.token ?? null;
  }

  /** True while a refresh token that has not expired is held (the access token itself may be stale; it is refreshed on demand). */
  hasValidSession(): boolean {
    const s = this.session();
    return !!s && new Date(s.refreshTokenExpiresAt).getTime() > Date.now();
  }

  login(request: LoginRequest): Observable<AuthUser> {
    return this.api.post<LoginResponse>(`${BASE}/login`, request, { skipErrorToast: true }).pipe(
      tap((res) => this.store(res, request.rememberMe)),
      map((res) => res.user),
    );
  }

  /** Revokes the refresh token server-side (best effort) and always clears the local session. */
  logout(): Observable<void> {
    const refreshToken = this.session()?.refreshToken;
    this.clear();
    if (!refreshToken) return of(undefined);
    return this.api.post<unknown>(`${BASE}/logout`, { refreshToken }, { skipErrorToast: true }).pipe(
      map(() => undefined),
      catchError(() => of(undefined)),
    );
  }

  /** The server revokes all other sessions and returns a fresh token pair, which replaces the current one. */
  changePassword(request: ChangePasswordRequest): Observable<AuthUser> {
    return this.api.post<LoginResponse>(`${BASE}/change-password`, request).pipe(
      tap((res) => this.store(res, this.session()?.remember ?? false)),
      map((res) => res.user),
    );
  }

  /** Keeps the cached user (shown in the topbar) in step after the user's own data changes, e.g. school name or logo. */
  patchUser(changes: Partial<AuthUser>): void {
    const current = this.session();
    if (!current) return;
    const next: AuthSession = { ...current, user: { ...current.user, ...changes } };
    this.session.set(next);
    this.write(current.remember ? 'local' : 'session', JSON.stringify(next));
  }

  /** Exchanges the refresh token for a new pair; emits the new access token. Single-flight. */
  refresh(): Observable<string> {
    const current = this.session();
    if (!current) return throwError(() => new Error('No session to refresh.'));

    if (!this.refreshing$) {
      this.refreshing$ = this.api.post<LoginResponse>(`${BASE}/refresh`, { refreshToken: current.refreshToken }, { skipErrorToast: true }).pipe(
        tap((res) => this.store(res, current.remember)),
        map((res) => res.token),
        finalize(() => (this.refreshing$ = null)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    }
    return this.refreshing$;
  }

  /** The session is unusable (refresh failed / expired): drop it and send the user to the login page. */
  endSession(): void {
    this.clear();
    if (!this.router.url.startsWith('/login')) {
      void this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
    }
  }

  // ── storage ─────────────────────────────────────────────────────────
  private store(res: LoginResponse, remember: boolean): void {
    const session: AuthSession = { ...res, remember };
    this.session.set(session);
    this.clearStorage();
    this.write(remember ? 'local' : 'session', JSON.stringify(session));
  }

  private clear(): void {
    this.session.set(null);
    this.clearStorage();
  }

  private load(): AuthSession | null {
    for (const kind of ['local', 'session'] as const) {
      try {
        const raw = this.storage(kind)?.getItem(STORAGE_KEY);
        if (!raw) continue;
        const parsed = JSON.parse(raw) as AuthSession;
        if (parsed?.token && parsed.refreshToken && parsed.user) return parsed;
      } catch {
        /* corrupt or inaccessible storage: treat as signed out */
      }
    }
    return null;
  }

  private storage(kind: 'local' | 'session'): Storage | null {
    try {
      return kind === 'local' ? localStorage : sessionStorage;
    } catch {
      return null; // storage blocked (private mode / policy)
    }
  }

  private write(kind: 'local' | 'session', value: string): void {
    try {
      this.storage(kind)?.setItem(STORAGE_KEY, value);
    } catch {
      /* quota / blocked: the in-memory session still works for this page load */
    }
  }

  private clearStorage(): void {
    for (const kind of ['local', 'session'] as const) {
      try {
        this.storage(kind)?.removeItem(STORAGE_KEY);
      } catch {
        /* ignore */
      }
    }
  }
}
