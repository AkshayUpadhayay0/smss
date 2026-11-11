import { api } from './index';
import type { AuthUser, LoginResponse } from './types';

// POST /api/Auth/login. rememberMe is always true on mobile: a 14-day refresh token instead of 1 day.
export async function login(loginId: string, password: string): Promise<LoginResponse> {
  const res = await api.request<LoginResponse>('/api/Auth/login', {
    method: 'POST',
    auth: false,
    body: { loginId: loginId.trim(), password, rememberMe: true },
  });
  await api.startSession(res);
  return res;
}

// GET /api/Auth/me (allowed while a password change is pending).
export const fetchMe = () => api.request<AuthUser>('/api/Auth/me');

// POST /api/Auth/change-password. The server revokes every other session and returns a FRESH token pair (isFirstLogin=false).
export async function changePassword(currentPassword: string, newPassword: string): Promise<LoginResponse> {
  const res = await api.request<LoginResponse>('/api/Auth/change-password', {
    method: 'POST',
    body: { currentPassword, newPassword },
  });
  await api.startSession(res);
  return res;
}

// POST /api/Auth/logout (anonymous, idempotent). The caller swallows failures: sign-out always clears local storage.
export async function logout(refreshToken: string): Promise<void> {
  await api.request<unknown>('/api/Auth/logout', { method: 'POST', auth: false, body: { refreshToken } });
}
