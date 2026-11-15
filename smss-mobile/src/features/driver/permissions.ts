// The permission state machine for driver duty. Pure: no React Native imports, so it is unit-tested. The native calls live in permissionsNative.ts.
//
// Order is fixed and each step needs the one before it:  disclosure  ->  foreground location  ->  background location ("Allow all the time")  ->  notifications (Android 13+).
// The disclosure is ALWAYS shown before the first system prompt.

export type PermState = 'granted' | 'denied' | 'blocked' | 'undetermined';
export type PermKey = 'foreground' | 'background' | 'notifications';
export type Gate = 'disclosure' | 'permissions' | 'ready';

export interface PermSnapshot {
  foreground: PermState;
  background: PermState;
  notifications: PermState;
}

export const STEP_ORDER: readonly PermKey[] = ['foreground', 'background', 'notifications'];

/** POST_NOTIFICATIONS is a runtime permission from Android 13 (API 33). Before that notifications simply work. */
export const notificationsRequired = (androidApi: number): boolean => androidApi >= 33;

/** From Android 11 (API 30) the system no longer shows a dialog for "Allow all the time": the app can only send the user to the Settings page. */
export const backgroundNeedsSettings = (androidApi: number): boolean => androidApi >= 30;

/** The steps still to do, in order. */
export function missingSteps(s: PermSnapshot, androidApi: number): PermKey[] {
  return STEP_ORDER.filter((k) => (k === 'notifications' && !notificationsRequired(androidApi) ? false : s[k] !== 'granted'));
}

/** The one step to show next, or null when everything needed is granted. Background is never offered before foreground. */
export function nextStep(s: PermSnapshot, androidApi: number): PermKey | null {
  return missingSteps(s, androidApi)[0] ?? null;
}

export const canShareLocation = (s: PermSnapshot, androidApi: number): boolean => missingSteps(s, androidApi).length === 0;

export function gateFor(disclosureAccepted: boolean, s: PermSnapshot, androidApi: number): Gate {
  if (!disclosureAccepted) return 'disclosure';
  return canShareLocation(s, androidApi) ? 'ready' : 'permissions';
}

/** expo-location: denied + canAskAgain=false means the system will not show the prompt any more ("denied forever"). */
export function fromLocationStatus(status: string, canAskAgain: boolean): PermState {
  if (status === 'granted') return 'granted';
  if (status === 'undetermined') return 'undetermined';
  return canAskAgain ? 'denied' : 'blocked';
}

/** React Native's PermissionsAndroid result. */
export function fromAndroidResult(result: string): PermState {
  if (result === 'granted') return 'granted';
  if (result === 'never_ask_again') return 'blocked';
  return 'denied';
}

export type StepAction = 'request' | 'openSettings';

export interface StepGuide {
  title: string;
  /** Why we ask, in plain words. */
  why: string;
  /** Exactly what to tap. */
  tap: string;
  action: StepAction;
  actionLabel: string;
  /** Shown when the driver has said no before. */
  retryNote?: string;
}

/** What the permission screen shows for a step. `state` decides whether the button asks again or sends the driver to Settings. */
export function guideFor(step: PermKey, state: PermState, androidApi: number): StepGuide {
  const blocked = state === 'blocked';
  const denied = state === 'denied' || blocked;
  switch (step) {
    case 'foreground':
      return {
        title: 'Step 1 of 3: Location while you use the app',
        why: 'SMSS needs your phone location to show your school where this vehicle is while you are on duty.',
        tap: blocked ? 'Tap "Open settings", choose Permissions > Location, then choose "Allow only while using the app" and keep "Use precise location" on.' : 'When Android asks, tap "While using the app" and keep "Precise" selected.',
        action: blocked ? 'openSettings' : 'request',
        actionLabel: blocked ? 'Open settings' : 'Allow location',
        retryNote: denied ? 'Location was not allowed. Duty cannot start without it.' : undefined,
      };
    case 'background':
      return backgroundNeedsSettings(androidApi)
        ? {
            title: 'Step 2 of 3: Location all the time',
            why: 'So sharing keeps working when you switch to another app or lock the screen during your trip. It is used only while you are on duty.',
            tap: 'Android will open the app Settings page (Android 11 and newer does this itself). Tap Permissions > Location, then choose "Allow all the time". Then come back to SMSS.',
            action: 'openSettings',
            actionLabel: 'Open settings',
            retryNote: denied ? 'Location is still not set to "Allow all the time".' : undefined,
          }
        : {
            title: 'Step 2 of 3: Location all the time',
            why: 'So sharing keeps working when you switch to another app or lock the screen during your trip. It is used only while you are on duty.',
            tap: blocked ? 'Tap "Open settings", choose Permissions > Location, then choose "Allow all the time".' : 'When Android asks, tap "Allow all the time".',
            action: blocked ? 'openSettings' : 'request',
            actionLabel: blocked ? 'Open settings' : 'Allow all the time',
            retryNote: denied ? 'Location is still not set to "Allow all the time".' : undefined,
          };
    case 'notifications':
      return {
        title: 'Step 3 of 3: Notifications',
        why: 'While you are on duty Android shows a notification so you can always see that location is being shared. Without this permission that notification is hidden.',
        tap: blocked ? 'Tap "Open settings", choose Notifications, and switch them on for SMSS.' : 'When Android asks, tap "Allow".',
        action: blocked ? 'openSettings' : 'request',
        actionLabel: blocked ? 'Open settings' : 'Allow notifications',
        retryNote: denied ? 'Notifications are off for SMSS.' : undefined,
      };
  }
}
