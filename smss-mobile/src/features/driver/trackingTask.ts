import * as Location from 'expo-location';
import * as TaskManager from 'expo-task-manager';
import { postLocation } from './dutyHttp';
import { dutyStore } from './nativeStore';
import type { DeviceLocation, StopReason } from './ping';
import { runPing, type PingDeps } from './trackingCore';

/** The background task. DEFINED AT MODULE SCOPE and imported from the app entry (index.ts): Android may start the app with no UI just to run it. */
export const TASK_NAME = 'smss-duty-location';

async function stopService(): Promise<void> {
  try {
    if (await Location.hasStartedLocationUpdatesAsync(TASK_NAME)) await Location.stopLocationUpdatesAsync(TASK_NAME);
  } catch {
    // nothing running, or the OS already stopped it
  }
}

/** The real dependencies of one ping. The token is read from secure storage inside runPing on EVERY call. */
export const nativePingDeps: PingDeps = {
  readToken: () => dutyStore.readToken(),
  send: (token, body) => postLocation(token, body),
  onStop: async (reason: StopReason) => {
    await stopService();
    await dutyStore.clearToken();
    await dutyStore.writeStopped(reason);
  },
};

TaskManager.defineTask(TASK_NAME, async ({ data, error }) => {
  if (error) return; // never log: the error object can carry device detail
  const locations = (data as { locations?: DeviceLocation[] } | undefined)?.locations;
  await runPing(nativePingDeps, locations); // never throws
});
