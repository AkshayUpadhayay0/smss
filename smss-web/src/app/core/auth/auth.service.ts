import { Injectable, computed, signal } from '@angular/core';
import { Organization, RegisterPayload, RegisteredAccount, Session, ThemeName, User } from '../models';
import { readStorage, removeStorage, STORAGE_KEYS, writeStorage } from '../utils/storage.util';
import { generateId } from '../utils/id.util';
import { OrganizationService } from '../services/organization.service';
import { ThemeService } from '../services/theme.service';

const SESSION_TTL_MS = 1000 * 60 * 60 * 12; // 12 hours

/**
 * Mock/local authentication. Everything here is designed to be replaced
 * by real HTTP calls later — components only ever talk to this service,
 * never to localStorage directly.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly sessionSignal = signal<Session | null>(this.restoreSession());

  readonly session = computed(() => this.sessionSignal());
  readonly currentUser = computed(() => this.sessionSignal()?.user ?? null);
  readonly currentOrganization = computed(() => this.sessionSignal()?.organization ?? null);
  readonly isAuthenticated = computed(() => this.sessionSignal() !== null);

  constructor(
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

  login(email: string, password: string, rememberMe: boolean): { success: boolean; error?: string } {
    const account = this.getAccounts().find((a) => a.user.email.toLowerCase() === email.toLowerCase());

    if (!account) {
      return { success: false, error: 'No account found with this email address.' };
    }
    if (account.passwordHash !== password) {
      return { success: false, error: 'Incorrect password. Please try again.' };
    }

    const organization = this.organizationService.getById(account.organization.id);
    if (!organization) {
      return { success: false, error: 'Organization data could not be found.' };
    }

    const session: Session = {
      user: account.user,
      organization,
      token: generateId('token'),
      expiresAt: Date.now() + (rememberMe ? SESSION_TTL_MS * 14 : SESSION_TTL_MS),
    };

    writeStorage(STORAGE_KEYS.session, session);
    this.sessionSignal.set(session);
    this.themeService.applyTheme(organization.theme);

    return { success: true };
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
