import { Injectable, signal } from '@angular/core';
import { CurrentUser, UserRole } from '../models/school.models';

const STORAGE_KEY = 'smt-auth-user';

const MOCK_USER: CurrentUser = {
  id: 'user-1',
  name: 'Priya Sharma',
  email: 'priya.sharma@greenvalley.edu.in',
  role: 'Admin',
  avatar: `data:image/svg+xml,${encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80"><rect width="80" height="80" rx="40" fill="#4f46e5"/><text x="50%" y="54%" font-family="Inter,Arial" font-size="30" fill="white" text-anchor="middle">PS</text></svg>'
  )}`,
};

@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly currentUser = signal<CurrentUser | null>(this.load());
  readonly isAuthenticated = signal<boolean>(!!this.load());

  login(email: string, _password: string, role: UserRole = 'Admin'): void {
    const user: CurrentUser = { ...MOCK_USER, email, role };
    this.currentUser.set(user);
    this.isAuthenticated.set(true);
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    }
  }

  logout(): void {
    this.currentUser.set(null);
    this.isAuthenticated.set(false);
    if (typeof localStorage !== 'undefined') {
      localStorage.removeItem(STORAGE_KEY);
    }
  }

  private load(): CurrentUser | null {
    if (typeof localStorage === 'undefined') return null;
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }
}
