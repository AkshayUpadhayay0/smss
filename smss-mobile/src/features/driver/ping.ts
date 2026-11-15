// What to send for a location fix and what to do with the server's answer. Pure (no React Native imports): unit-tested.
// The table in smss-mobile/CLAUDE.md ("Driver duty: what each answer makes the app do") is this file in words. Keep them in step.

export interface DeviceLocation {
  coords: { latitude: number; longitude: number; accuracy?: number | null; speed?: number | null; heading?: number | null };
  /** Milliseconds since the epoch (expo-location). */
  timestamp: number;
  /** Android: true when the fix came from a mock provider (an emulator route playback or a spoofing app). */
  mocked?: boolean;
}

export interface PingBody {
  latitude: number;
  longitude: number;
  accuracyMeters: number | null;
  speedMps: number | null;
  headingDegrees: number | null;
  recordedAt: string;
  mocked: boolean;
}

export type StopReason = 'UNKNOWN_TOKEN' | 'SESSION_ENDED' | 'SESSION_EXPIRED' | 'DRIVER_DEACTIVATED' | 'NO_TOKEN';
export type PingAction = 'OK' | 'DROP' | 'RETRY' | 'STOP';

export type PingHttpResult = { kind: 'response'; status: number; body: unknown } | { kind: 'network' };

export interface PingOutcome {
  action: PingAction;
  stopReason?: StopReason;
  /** The server's data-quality reason for a DROP (LOW_ACCURACY, STALE_FIX, ...). */
  reason?: string;
}

const STOP_CODES: readonly string[] = ['SESSION_ENDED', 'SESSION_EXPIRED', 'DRIVER_DEACTIVATED'];

/** The newest fix of a batch: the task may deliver several, only the latest is worth sending (latest-only, no history). */
export function newestLocation(locations: readonly DeviceLocation[] | null | undefined): DeviceLocation | null {
  if (!locations || locations.length === 0) return null;
  return locations.reduce((a, b) => (b.timestamp >= a.timestamp ? b : a));
}

const finiteOrNull = (v: number | null | undefined): number | null => (typeof v === 'number' && Number.isFinite(v) ? v : null);

/** expo-location reports "unknown" as -1 for speed, heading and accuracy: the server must get null, not a negative number (it answers 400 for those). */
const nonNegativeOrNull = (v: number | null | undefined): number | null => {
  const n = finiteOrNull(v);
  return n !== null && n >= 0 ? n : null;
};

const round6 = (v: number): number => Math.round(v * 1e6) / 1e6;

export function buildPingBody(loc: DeviceLocation): PingBody {
  const heading = nonNegativeOrNull(loc.coords.heading);
  return {
    latitude: round6(loc.coords.latitude),
    longitude: round6(loc.coords.longitude),
    accuracyMeters: nonNegativeOrNull(loc.coords.accuracy),
    speedMps: nonNegativeOrNull(loc.coords.speed),
    headingDegrees: heading !== null && heading <= 360 ? heading : null,
    recordedAt: new Date(loc.timestamp).toISOString(),
    mocked: loc.mocked === true,
  };
}

function dataOf(body: unknown): { accepted?: unknown; reason?: unknown; code?: unknown } {
  const d = body && typeof body === 'object' ? (body as { data?: unknown }).data : null;
  return d && typeof d === 'object' ? (d as { accepted?: unknown; reason?: unknown; code?: unknown }) : {};
}

/**
 * STOP  : the session cannot continue (401, 409). Stop the service, delete the token, show "duty ended".
 * RETRY : transient (no network, 5xx, 429, anything unexpected). Do nothing: the next scheduled ping sends a fresh fix.
 * DROP  : the data was unusable (200 accepted:false, or a 400 for a bad value). Do nothing: the next ping sends another.
 * OK    : accepted.
 * No offline queue: RETRY and DROP both simply mean "wait for the next ping".
 */
export function classifyPing(result: PingHttpResult): PingOutcome {
  if (result.kind === 'network') return { action: 'RETRY' };
  const { status, body } = result;
  const data = dataOf(body);

  if (status === 200) {
    return data.accepted === false ? { action: 'DROP', reason: typeof data.reason === 'string' ? data.reason : undefined } : { action: 'OK' };
  }
  if (status === 401) return { action: 'STOP', stopReason: 'UNKNOWN_TOKEN' };
  if (status === 409) {
    const code = typeof data.code === 'string' ? data.code : '';
    return { action: 'STOP', stopReason: (STOP_CODES.includes(code) ? code : 'SESSION_ENDED') as StopReason };
  }
  if (status === 400) return { action: 'DROP' };
  return { action: 'RETRY' }; // 429, 5xx and anything else unexpected (a proxy error page, a 404 during a deploy)
}
