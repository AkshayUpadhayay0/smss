import { HttpClient } from '@angular/common/http';
import { Injectable, computed, signal } from '@angular/core';
import { Observable, finalize, map, of, shareReplay, tap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ApiEnvelope,
  ChangePasswordPayload,
  LoginPayload,
  LoginResponse,
  Organization,
  RegisterPayload,
  RegisteredAccount,
  Session,
  ThemeName,
  User,
} from '../models';
import { readStorage, removeStorage, STORAGE_KEYS, writeStorage } from '../utils/storage.util';
import { generateId } from '../utils/id.util';
import { OrganizationService } from '../services/organization.service';
import { ThemeService } from '../services/theme.service';

const SESSION_TTL_MS = 1000 * 60 * 60 * 12; // 12 hours

/**
 * Authentication against the API (api/Auth/*). Components only ever talk to
 * this service, never to localStorage directly. `register` is still the local
 * mock from the template — real school registration happens via the
 * SchoolRegistration API.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly authUrl = `${environment.apiUrl}/api/Auth`;
  private refreshInFlight$: Observable<Session> | null = null;

  private readonly sessionSignal = signal<Session | null>(this.restoreSession());

  readonly session = computed(() => this.sessionSignal());
  readonly currentUser = computed(() => this.sessionSignal()?.user ?? null);
  readonly currentOrganization = computed(() => this.sessionSignal()?.organization ?? null);
  readonly isAuthenticated = computed(() => this.sessionSignal() !== null);
  /** True until the user replaces the temporary password issued at school registration. */
  readonly mustChangePassword = computed(() => this.sessionSignal()?.isFirstLogin === true);

  constructor(
    private readonly http: HttpClient,
    private readonly organizationService: OrganizationService,
    private readonly themeService: ThemeService,
  ) {
    const session = this.sessionSignal();
    if (session) {
      this.themeService.applyTheme(session.organization.theme);
    } else {
      this.themeService.applyTheme('default');
    }
  }

  private getAccounts(): RegisteredAccount[] {
    return readStorage<RegisteredAccount[]>(STORAGE_KEYS.accounts, []);
  }

  private saveAccounts(accounts: RegisteredAccount[]): void {
    writeStorage(STORAGE_KEYS.accounts, accounts);
  }

  private restoreSession(): Session | null {
    const session = readStorage<Session | null>(STORAGE_KEYS.session, null);
    if (!session) return null;
    if (session.expiresAt < Date.now()) {
      removeStorage(STORAGE_KEYS.session);
      return null;
    }
    return session;
  }

  emailExists(email: string): boolean {
    return this.getAccounts().some((a) => a.user.email.toLowerCase() === email.toLowerCase());
  }

  /** Emits the new session on success; errors surface as HttpErrorResponse (see `errorMessage`). */
  login(payload: LoginPayload): Observable<Session> {
    return this.http.post<ApiEnvelope<LoginResponse>>(`${this.authUrl}/login`, payload).pipe(
      map((res) => this.toSession(res.data)),
      tap((session) => this.storeSession(session)),
    );
  }

  /**
   * Exchanges the refresh token for a new token pair. Concurrent callers (several requests
   * failing with 401 at once) share a single HTTP call, because refresh tokens are single-use.
   */
  refreshSession(): Observable<Session> {
    if (this.refreshInFlight$) return this.refreshInFlight$;

    const current = this.sessionSignal();
    if (!current?.refreshToken) return throwError(() => new Error('No refresh token available.'));

    // Another browser tab may already have rotated the token; adopt its session instead of replaying ours
    const stored = readStorage<Session | null>(STORAGE_KEYS.session, null);
    if (stored?.refreshToken && stored.refreshToken !== current.refreshToken) {
      this.sessionSignal.set(stored);
      return of(stored);
    }

    this.refreshInFlight$ = this.http
      .post<ApiEnvelope<LoginResponse>>(`${this.authUrl}/refresh`, { refreshToken: current.refreshToken })
      .pipe(
        map((res) => this.toSession(res.data)),
        tap((session) => this.storeSession(session)),
        finalize(() => (this.refreshInFlight$ = null)),
        shareReplay(1),
      );
    return this.refreshInFlight$;
  }

  /** Replaces the temporary/old password. The API ends other sessions and returns a fresh token pair for this one. */
  changePassword(payload: ChangePasswordPayload): Observable<void> {
    return this.http.post<ApiEnvelope<LoginResponse>>(`${this.authUrl}/change-password`, payload).pipe(
      tap((res) => this.storeSession(this.toSession(res.data))),
      map(() => undefined),
    );
  }

  private storeSession(session: Session): void {
    writeStorage(STORAGE_KEYS.session, session);
    this.sessionSignal.set(session);
    this.themeService.applyTheme(session.organization.theme);
  }

  /** Pulls the API's validation/business message out of an error response. */
  errorMessage(error: unknown, fallback = 'Unable to reach the server. Please try again.'): string {
    const body = (error as { error?: { message?: string; data?: Record<string, string[]> } })?.error;
    const firstFieldError = body?.data && typeof body.data === 'object' ? Object.values(body.data)[0]?.[0] : undefined;
    return firstFieldError ?? body?.message ?? fallback;
  }

  private toSession(res: LoginResponse): Session {
    const u = res.user;
    const user: User = {
      id: String(u.userId),
      organizationId: u.schoolId ?? '',
      fullName: u.schoolName ?? u.username,
      email: u.email ?? '',
      role: u.roles[0] ?? u.userType,
      avatarUrl: u.logoUrl ?? undefined,
      status: 'Active',
      createdAt: new Date().toISOString(),
    };
    const organization: Organization = {
      id: u.schoolId ?? '',
      name: u.schoolName ?? u.username,
      code: u.schoolCode ?? u.username,
      logo: u.logoUrl ?? undefined,
      theme: 'default',
      createdAt: new Date().toISOString(),
    };
    return {
      user,
      organization,
      token: res.token,
      refreshToken: res.refreshToken,
      // The session as a whole lives as long as the refresh token; the short-lived access token is renewed on 401
      expiresAt: new Date(res.refreshTokenExpiresAt).getTime(),
      isFirstLogin: u.isFirstLogin,
    };
  }

  register(payload: RegisterPayload): { success: boolean; error?: string } {
    if (this.emailExists(payload.adminEmail)) {
      return { success: false, error: 'An account with this email already exists.' };
    }
    const orgService = this.organizationService;
    if (orgService.codeExists(payload.organizationCode)) {
      return { success: false, error: 'This organization code is already taken.' };
    }

    const theme = payload.theme as ThemeName;
    const organization: Organization = orgService.create(payload.organizationName, payload.organizationCode, theme);

    const user: User = {
      id: generateId('user'),
      organizationId: organization.id,
      fullName: payload.adminFullName,
      email: payload.adminEmail,
      role: 'Administrator',
      status: 'Active',
      createdAt: new Date().toISOString(),
    };

    const account: RegisteredAccount = {
      organization: {
        id: organization.id,
        name: organization.name,
        code: organization.code,
        theme: organization.theme,
      },
      user,
      passwordHash: payload.password,
    };

    const accounts = this.getAccounts();
    accounts.push(account);
    this.saveAccounts(accounts);

    const session: Session = {
      user,
      organization,
      token: generateId('token'),
      expiresAt: Date.now() + SESSION_TTL_MS,
    };
    writeStorage(STORAGE_KEYS.session, session);
    this.sessionSignal.set(session);
    this.themeService.applyTheme(organization.theme);

    return { success: true };
  }

  logout(): void {
    // Best effort: revoke the refresh token server-side, but never block or fail the local sign-out
    const refreshToken = this.sessionSignal()?.refreshToken;
    if (refreshToken) {
      this.http.post(`${this.authUrl}/logout`, { refreshToken }).subscribe({ error: () => undefined });
    }

    removeStorage(STORAGE_KEYS.session);
    this.sessionSignal.set(null);
    this.themeService.applyTheme('default');
  }

  /** Called when the user changes theme from within the app (e.g. Settings). */
  updateActiveOrganizationTheme(theme: ThemeName): void {
    const session = this.sessionSignal();
    if (!session) return;

    this.organizationService.updateTheme(session.organization.id, theme);
    const updatedSession: Session = {
      ...session,
      organization: { ...session.organization, theme },
    };
    writeStorage(STORAGE_KEYS.session, updatedSession);
    this.sessionSignal.set(updatedSession);
    this.themeService.applyTheme(theme);
  }
}
