import { AuthSession, AuthUser, LoginResponse } from '../models/auth.model';

export const SESSION_KEY = 'smss.session';

export function makeUser(overrides: Partial<AuthUser> = {}): AuthUser {
  return {
    userId: 1,
    username: 'Akshay Upadhayay',
    userType: 'Super Admin',
    email: 'admin@example.com',
    schoolId: 'SUAD0000001',
    schoolCode: 'SUAD01',
    schoolName: 'SMSS Admin',
    logoUrl: null,
    roles: ['Super Admin'],
    isFirstLogin: false,
    ...overrides,
  };
}

export function makeLoginResponse(overrides: Partial<LoginResponse> = {}): LoginResponse {
  const hour = 60 * 60 * 1000;
  return {
    token: 'access-1',
    expiresAt: new Date(Date.now() + hour).toISOString(),
    refreshToken: 'refresh-1',
    refreshTokenExpiresAt: new Date(Date.now() + 24 * hour).toISOString(),
    user: makeUser(),
    ...overrides,
  };
}

/** Puts a session in storage BEFORE AuthService is first injected (it reads storage on construction). */
export function seedSession(overrides: Partial<AuthSession> = {}, where: 'local' | 'session' = 'session'): AuthSession {
  const session: AuthSession = { ...makeLoginResponse(), remember: where === 'local', ...overrides };
  (where === 'local' ? localStorage : sessionStorage).setItem(SESSION_KEY, JSON.stringify(session));
  return session;
}

export function clearSessions(): void {
  localStorage.removeItem(SESSION_KEY);
  sessionStorage.removeItem(SESSION_KEY);
}
