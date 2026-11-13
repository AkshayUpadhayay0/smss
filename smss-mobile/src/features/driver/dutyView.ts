import { canShareLocation, type PermSnapshot } from './permissions';
import type { StopReason } from './ping';

// Pure view logic for the driver Home. The server is the authority on whether duty is open (me.duty.onDuty); the phone only knows whether the
// location service is running and why it last stopped. Nothing here reads the phone's clock: ages come from the server.

export type DutyStatus = 'LIVE' | 'STALE' | 'OFFLINE';

export interface DriverMe {
  driverId: string;
  fullName: string;
  vehicle: { vehicleId: number; vehicleLabel: string; vehiclePlate: string; isActive: boolean } | null;
  areas: { areaId: number; areaName: string }[];
  duty: { onDuty: boolean; sessionId: number | null; startedAt: string | null; status: DutyStatus | null; lastSentAt: string | null; lastSentAgeSeconds: number | null };
  canStartDuty: boolean;
  startBlockedReason: string | null;
  intervalSeconds: number;
  minIntervalSeconds: number;
  serverTime: string;
}

export type DutyView =
  | { kind: 'OFF' }
  /** The server says on duty and the location service is running. */
  | { kind: 'SHARING' }
  /** The server says on duty but the service is NOT running (phone restarted, app killed, permission taken away): offer Resume. */
  | { kind: 'NOT_SHARING' }
  /** Sharing stopped because the server ended the session (or the token was refused). */
  | { kind: 'ENDED'; reason: StopReason };

export function deriveDutyView(input: { serverOnDuty: boolean; serviceRunning: boolean; stopped: StopReason | null }): DutyView {
  const { serverOnDuty, serviceRunning, stopped } = input;
  if (serverOnDuty) return serviceRunning ? { kind: 'SHARING' } : { kind: 'NOT_SHARING' };
  return stopped ? { kind: 'ENDED', reason: stopped } : { kind: 'OFF' };
}

/** null when "Mark today's attendance" can be pressed; otherwise the plain-words reason it is disabled. */
export function startBlockedMessage(me: Pick<DriverMe, 'canStartDuty' | 'startBlockedReason'> | null | undefined, perms: PermSnapshot | null, androidApi: number): string | null {
  if (!me) return 'Loading your details…';
  if (!me.canStartDuty) return me.startBlockedReason ?? 'Duty cannot be started right now.';
  if (!perms || !canShareLocation(perms, androidApi)) return 'Allow location access to start duty. Tap "Set up location sharing" below.';
  return null;
}

/** "08:45" in the phone's time zone, from a server timestamp. */
export function formatClock(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

/** "Last sent 40 s ago", from the age the SERVER reported. */
export function lastSentText(ageSeconds: number | null | undefined): string {
  if (ageSeconds == null) return 'Nothing sent yet';
  const s = Math.max(0, Math.round(ageSeconds));
  if (s < 10) return 'Last sent just now';
  if (s < 60) return `Last sent ${s} s ago`;
  if (s < 3600) return `Last sent ${Math.floor(s / 60)} min ago`;
  return `Last sent ${Math.floor(s / 3600)} h ago`;
}

export const STATUS_TEXT: Record<DutyStatus, string> = { LIVE: 'Live', STALE: 'Stale', OFFLINE: 'Offline' };
export const STATUS_HELP: Record<DutyStatus, string> = {
  LIVE: 'Your school can see you now.',
  STALE: 'Your last location is a few minutes old.',
  OFFLINE: 'Your school has not heard from this phone for a while.',
};
