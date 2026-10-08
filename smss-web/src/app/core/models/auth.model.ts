export interface LoginRequest {
  username: string;
  password: string;
  rememberMe: boolean;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface AuthUser {
  userId: number;
  username: string;
  userType: string;
  email?: string | null;
  schoolId?: string | null;
  schoolCode?: string | null;
  schoolName?: string | null;
  logoUrl?: string | null;
  roles: string[];
  isFirstLogin: boolean;
}

/** Returned by login, refresh and change-password. */
export interface LoginResponse {
  token: string;
  expiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUser;
}

/** What we keep client-side. `remember` decides localStorage (true) vs sessionStorage (false). */
export interface AuthSession extends LoginResponse {
  remember: boolean;
}
