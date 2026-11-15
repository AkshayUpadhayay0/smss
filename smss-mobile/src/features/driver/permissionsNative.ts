import * as Location from 'expo-location';
import { Linking, PermissionsAndroid, Platform } from 'react-native';
import { fromAndroidResult, fromLocationStatus, notificationsRequired, type PermKey, type PermSnapshot, type PermState } from './permissions';

/** The Android API level (33 = Android 13). 0 on other platforms: notifications and settings-only background rules then do not apply. */
export const androidApi: number = Platform.OS === 'android' ? (typeof Platform.Version === 'number' ? Platform.Version : parseInt(String(Platform.Version), 10) || 0) : 0;

const POST_NOTIFICATIONS = 'android.permission.POST_NOTIFICATIONS' as const;

// PermissionsAndroid.check only answers yes / no. "Denied forever" is learned from the answer to a request, so it is remembered here.
let notificationsBlocked = false;

async function notificationsState(): Promise<PermState> {
  if (!notificationsRequired(androidApi)) return 'granted';
  if (await PermissionsAndroid.check(POST_NOTIFICATIONS)) {
    notificationsBlocked = false;
    return 'granted';
  }
  return notificationsBlocked ? 'blocked' : 'denied';
}

/** The current state of the three permissions. Cheap: call it whenever the screen comes back into view (the driver may have used Settings). */
export async function readPermissions(): Promise<PermSnapshot> {
  const [fg, bg, notifications] = await Promise.all([Location.getForegroundPermissionsAsync(), Location.getBackgroundPermissionsAsync(), notificationsState()]);
  return {
    foreground: fromLocationStatus(fg.status, fg.canAskAgain),
    background: fromLocationStatus(bg.status, bg.canAskAgain),
    notifications,
  };
}

/** Shows the system prompt for one step (or, for background location on Android 11+, opens the app's Settings page: that is how Android works). Re-read afterwards. */
export async function requestStep(step: PermKey): Promise<void> {
  if (step === 'foreground') {
    await Location.requestForegroundPermissionsAsync();
  } else if (step === 'background') {
    await Location.requestBackgroundPermissionsAsync();
  } else if (notificationsRequired(androidApi)) {
    const result = await PermissionsAndroid.request(POST_NOTIFICATIONS);
    notificationsBlocked = fromAndroidResult(result) === 'blocked';
  }
}

export const openAppSettings = (): Promise<void> => Linking.openSettings();

/**
 * Opens the system's battery-optimisation LIST, where the driver chooses SMSS. This is a plain settings screen: the app does NOT hold
 * REQUEST_IGNORE_BATTERY_OPTIMIZATIONS and never asks to be exempted (Google Play restricts that permission).
 */
export async function openBatterySettings(): Promise<void> {
  try {
    await Linking.sendIntent('android.settings.IGNORE_BATTERY_OPTIMIZATION_SETTINGS');
  } catch {
    await Linking.openSettings();
  }
}
