import { Organization } from './organization.model';
import { User } from './user.model';

export interface LoginPayload {
  username: string;
  password: string;
  rememberMe: boolean;
}

export interface RegisterPayload {
  organizationName: string;
  organizationCode: string;
  adminFullName: string;
  adminEmail: string;
  password: string;
  confirmPassword: string;
  phone: string;
  theme: string;
  acceptTerms: boolean;
}

export interface Session {
  user: User;
  organization: Organization;
  token: string;
  refreshToken?: string;
  expiresAt: number;
  isFirstLogin?: boolean;
}

// ---- API contract (api/Auth/*) ----
export interface ApiEnvelope<T> {
  status: boolean;
  statusCode: number;
  message: string;
  data: T;
}

export interface AuthUserResponse {
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

export interface LoginResponse {
  token: string;
  expiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUserResponse;
}

export interface ChangePasswordPayload {
  currentPassword: string;
  newPassword: string;
}
