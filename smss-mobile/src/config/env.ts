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

/** DEV builds only: a shorter duty interval in seconds (1..600), or null. Anything else (empty, not a number, out of range) is ignored. */
export function parseDutyIntervalOverride(raw: unknown): number | null {
  const n = typeof raw === 'string' && raw.trim() !== '' ? Number(raw) : typeof raw === 'number' ? raw : NaN;
  return Number.isInteger(n) && n >= 1 && n <= 600 ? n : null;
}

/** DEV builds only: 'high' (GPS) instead of the production Balanced accuracy, because the Android emulator has no network location. Anything else is ignored. */
export function parseDutyAccuracyOverride(raw: unknown): 'high' | null {
  return typeof raw === 'string' && raw.trim().toLowerCase() === 'high' ? 'high' : null;
}

export const env = {
  get dutyAccuracyOverride(): 'high' | null {
    return parseDutyAccuracyOverride(Constants.expoConfig?.extra?.dutyAccuracy);
  },
  get dutyIntervalOverrideSeconds(): number | null {
    return parseDutyIntervalOverride(Constants.expoConfig?.extra?.dutyIntervalSeconds);
  },
  get apiBaseUrl(): string {
    return resolveApiBaseUrl(Constants.expoConfig?.extra?.apiBaseUrl, __DEV__);
  },
};
