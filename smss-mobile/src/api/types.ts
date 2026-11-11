/** The envelope every smss-api endpoint returns. */
export interface ApiEnvelope<T> {
  status: boolean;
  statusCode: number;
  message: string;
  data: T | null;
}

export interface AuthUser {
  userId: number;
  displayName: string;
  orgUserId: string;
  userType: string;
  email: string | null;
  schoolId: string | null;
  schoolCode: string | null;
  schoolName: string | null;
  logoUrl: string | null;
  /** Role NAMES as lut_roles holds them ("Student", "Teacher", "School Admin", ...). Only src/auth/roles.ts may interpret them. */
  roles: string[];
  isFirstLogin: boolean;
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUser;
}
