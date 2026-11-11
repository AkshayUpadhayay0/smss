import * as SecureStore from 'expo-secure-store';
import type { SessionStore, StoredSession } from '../api/client';
import type { AuthUser } from '../api/types';

// Tokens live in the OS keystore/keychain via expo-secure-store, never in AsyncStorage. Separate keys keep each value small
// (SecureStore warns above ~2 KB per value on iOS).
const KEY_ACCESS = 'smss.access';
const KEY_REFRESH = 'smss.refresh';
const KEY_USER = 'smss.user';

function isUser(v: unknown): v is AuthUser {
  const u = v as Partial<AuthUser> | null;
  return !!u && typeof u.userId === 'number' && typeof u.displayName === 'string' && Array.isArray(u.roles) && typeof u.isFirstLogin === 'boolean';
}

export const secureSessionStore: SessionStore = {
  async read(): Promise<StoredSession | null> {
    try {
      const [accessToken, refreshToken, userJson] = await Promise.all([
        SecureStore.getItemAsync(KEY_ACCESS),
        SecureStore.getItemAsync(KEY_REFRESH),
        SecureStore.getItemAsync(KEY_USER),
      ]);
      if (!accessToken || !refreshToken || !userJson) return null;
      const user: unknown = JSON.parse(userJson);
      return isUser(user) ? { accessToken, refreshToken, user } : null;
    } catch {
      return null; // unreadable storage = signed out
    }
  },
  async write(s: StoredSession): Promise<void> {
    await SecureStore.setItemAsync(KEY_ACCESS, s.accessToken);
    await SecureStore.setItemAsync(KEY_REFRESH, s.refreshToken);
    await SecureStore.setItemAsync(KEY_USER, JSON.stringify(s.user));
  },
  async clear(): Promise<void> {
    await Promise.allSettled([
      SecureStore.deleteItemAsync(KEY_ACCESS),
      SecureStore.deleteItemAsync(KEY_REFRESH),
      SecureStore.deleteItemAsync(KEY_USER),
    ]);
  },
};
