import { Organization } from './organization.model';
import { User } from './user.model';

export interface LoginPayload {
  email: string;
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
  expiresAt: number;
}
