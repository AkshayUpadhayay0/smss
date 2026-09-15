/**
 * Thin, typed wrapper around localStorage.
 * Centralizing storage access here means swapping the persistence layer
 * (e.g. to a real backend) later only requires changing the services
 * that call into this utility, not every component.
 */
export const STORAGE_KEYS = {
  organizations: 'smss.organizations',
  accounts: 'smss.accounts',
  session: 'smss.session',
  sidebarCollapsed: 'smss.sidebar.collapsed',
  seeded: 'smss.seeded',
} as const;

export function readStorage<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return fallback;
    return JSON.parse(raw) as T;
  } catch {
    return fallback;
  }
}

export function writeStorage<T>(key: string, value: T): void {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // localStorage may be unavailable (private browsing, quota, etc.) — fail silently.
  }
}

export function removeStorage(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // ignore
  }
}
