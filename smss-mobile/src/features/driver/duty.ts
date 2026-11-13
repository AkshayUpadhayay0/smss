import * as Location from 'expo-location';
import { env } from '../../config/env';
import { NOTIFICATION_BODY, NOTIFICATION_TITLE } from './copy';
import { endDutyRequest, startDutyRequest, type DutyStarted } from './dutyApi';
import { postEndWithToken } from './dutyHttp';
import { dutyStore } from './nativeStore';
import type { DeviceLocation } from './ping';
import { runPing } from './trackingCore';
import { nativePingDeps, TASK_NAME } from './trackingTask';

// The duty controller: start, resume, end. It talks to the server (login), the location service and secure storage. The pure rules are in the sibling modules.

export async function isSharing(): Promise<boolean> {
  try {
    return await Location.hasStartedLocationUpdatesAsync(TASK_NAME);
  } catch {
    return false;
  }
}

/** The interval the app uses: the server's (60 s), never below the server's minimum. A DEV build may shorten it for quick emulator passes (EXPO_PUBLIC_DUTY_INTERVAL_SECONDS). */
export function intervalSeconds(started: Pick<DutyStarted, 'intervalSeconds' | 'minIntervalSeconds'>): number {
  const override = __DEV__ ? env.dutyIntervalOverrideSeconds : null;
  return Math.max(started.minIntervalSeconds, override ?? started.intervalSeconds);
}

async function stopService(): Promise<void> {
  try {
    if (await isSharing()) await Location.stopLocationUpdatesAsync(TASK_NAME);
  } catch {
    // already stopped
  }
}

/** Stops the location service and deletes the tracking token. Always safe to call. */
export async function stopSharing(): Promise<void> {
  await stopService();
  await dutyStore.clearToken();
}

async function startService(seconds: number): Promise<void> {
  await Location.startLocationUpdatesAsync(TASK_NAME, {
    accuracy: Location.Accuracy.Balanced,
    timeInterval: seconds * 1000,
    distanceInterval: 0,
    pausesUpdatesAutomatically: false,
    showsBackgroundLocationIndicator: true,
    foregroundService: { notificationTitle: NOTIFICATION_TITLE, notificationBody: NOTIFICATION_BODY, notificationColor: '#6D28D9' },
  });
}

/** The first fix goes out at once (not after the first interval), through the same path as every later one. A failure here never fails duty: the next ping follows. */
async function sendFirstFix(): Promise<void> {
  try {
    const loc = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
    await runPing(nativePingDeps, [loc as DeviceLocation]);
  } catch {
    // no fix yet: the service will deliver one
  }
}

/**
 * "Mark today's attendance" (and "Resume"): starts duty on the server, keeps the NEW tracking token, starts the location service, sends the first fix.
 * A resume is the same call: the server answers 200 with a new token and the old one stops working.
 * Throws the server's ApiError (409 "No vehicle is assigned..." etc.) before anything is started on the phone.
 */
export async function startDuty(): Promise<DutyStarted> {
  const started = await startDutyRequest();
  await dutyStore.writeToken(started.trackingToken);
  await dutyStore.clearStopped();
  try {
    await stopService(); // a service from before a resume is replaced, never doubled
    await startService(intervalSeconds(started));
  } catch (e) {
    // The server has opened the session but the phone cannot share (a permission was taken away, the OS refused the service): do not leave it open.
    await endEverywhere();
    throw e;
  }
  await sendFirstFix();
  return started;
}

/** Ends the session on the server with the tracking token (works even when the login has expired). */
async function endWithToken(): Promise<boolean> {
  const token = await dutyStore.readToken();
  if (!token) return false;
  const r = await postEndWithToken(token);
  return r.kind === 'response' && r.status === 200;
}

async function endEverywhere(): Promise<boolean> {
  let ended = false;
  try {
    await endDutyRequest(); // the login first
    ended = true;
  } catch {
    ended = await endWithToken(); // the login may be expired: the tracking token can still end the session
  }
  await stopSharing();
  return ended;
}

/**
 * "End duty". Local sharing ALWAYS stops, even when the server cannot be reached (then the school sees the vehicle go OFFLINE and the session is
 * closed the next time duty starts). Returns whether the server confirmed.
 */
export async function endDuty(): Promise<{ serverEnded: boolean }> {
  await stopService(); // stop the pings first: nothing can arrive after the end
  const serverEnded = await endEverywhere();
  await dutyStore.clearStopped();
  return { serverEnded };
}
