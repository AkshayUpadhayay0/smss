import Constants from 'expo-constants';

/**
 * The API base URL, from app.config.ts `extra.apiBaseUrl` (which reads EXPO_PUBLIC_API_BASE_URL from .env / the EAS environment).
 * Never a literal in source. Throws a clear error at startup when it is missing or unsafe, instead of failing later with a confusing network error.
 */
export function resolveApiBaseUrl(raw: unknown, isDev: boolean): string {
  const value = typeof raw === 'string' ? raw.trim().replace(/\/+$/, '') : '';
  if (!value) {
    throw new Error('EXPO_PUBLIC_API_BASE_URL is not set. Copy .env.example to .env and set it (see CLAUDE.md).');
  }
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error('EXPO_PUBLIC_API_BASE_URL is not a valid URL.');
  }
  // Android release builds block cleartext traffic, and credentials must never travel in clear anyway.
  if (url.protocol !== 'https:' && !(isDev && url.protocol === 'http:')) {
    throw new Error('EXPO_PUBLIC_API_BASE_URL must be https:// (http:// is allowed in development builds only).');
  }
  return value;
}

export const env = {
  get apiBaseUrl(): string {
    return resolveApiBaseUrl(Constants.expoConfig?.extra?.apiBaseUrl, __DEV__);
  },
};
