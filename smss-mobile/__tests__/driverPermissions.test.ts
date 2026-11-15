import {
  backgroundNeedsSettings,
  canShareLocation,
  fromAndroidResult,
  fromLocationStatus,
  gateFor,
  guideFor,
  missingSteps,
  nextStep,
  notificationsRequired,
  type PermSnapshot,
} from '../src/features/driver/permissions';

const snap = (foreground: PermSnapshot['foreground'], background: PermSnapshot['background'], notifications: PermSnapshot['notifications']): PermSnapshot => ({ foreground, background, notifications });
const ALL = snap('granted', 'granted', 'granted');
const NONE = snap('undetermined', 'undetermined', 'undetermined');

describe('permission steps', () => {
  it('goes foreground, then background, then notifications, one at a time', () => {
    expect(nextStep(NONE, 35)).toBe('foreground');
    expect(nextStep(snap('granted', 'undetermined', 'undetermined'), 35)).toBe('background');
    expect(nextStep(snap('granted', 'granted', 'undetermined'), 35)).toBe('notifications');
    expect(nextStep(ALL, 35)).toBeNull();
  });

  it('never offers background before foreground is granted, even if the snapshot says background is granted', () => {
    expect(nextStep(snap('denied', 'granted', 'granted'), 35)).toBe('foreground');
    expect(missingSteps(snap('denied', 'denied', 'granted'), 35)).toEqual(['foreground', 'background']);
  });

  it('notifications are required from Android 13 (API 33) only', () => {
    expect(notificationsRequired(32)).toBe(false);
    expect(notificationsRequired(33)).toBe(true);
    expect(nextStep(snap('granted', 'granted', 'undetermined'), 32)).toBeNull();
    expect(canShareLocation(snap('granted', 'granted', 'denied'), 32)).toBe(true);
    expect(canShareLocation(snap('granted', 'granted', 'denied'), 34)).toBe(false);
  });

  it('background location needs the Settings page from Android 11 (API 30)', () => {
    expect(backgroundNeedsSettings(29)).toBe(false);
    expect(backgroundNeedsSettings(30)).toBe(true);
    expect(guideFor('background', 'undetermined', 35).action).toBe('openSettings');
    expect(guideFor('background', 'undetermined', 35).tap).toContain('Allow all the time');
    expect(guideFor('background', 'undetermined', 29).action).toBe('request');
  });

  it.each([
    [snap('granted', 'granted', 'denied'), false],
    [snap('granted', 'blocked', 'granted'), false],
    [snap('denied', 'granted', 'granted'), false],
    [ALL, true],
  ])('canShareLocation %j on Android 15 is %s', (s, expected) => expect(canShareLocation(s, 35)).toBe(expected));
});

describe('the gate (what the driver sees first)', () => {
  it('shows the disclosure first, whatever the permissions are', () => {
    expect(gateFor(false, NONE, 35)).toBe('disclosure');
    expect(gateFor(false, ALL, 35)).toBe('disclosure');
  });
  it('then the permission steps, then Home', () => {
    expect(gateFor(true, NONE, 35)).toBe('permissions');
    expect(gateFor(true, snap('granted', 'granted', 'denied'), 35)).toBe('permissions');
    expect(gateFor(true, ALL, 35)).toBe('ready');
  });
  it('sends the driver back to permissions when one is taken away later', () => {
    expect(gateFor(true, snap('granted', 'denied', 'granted'), 35)).toBe('permissions');
  });
});

describe('mapping native answers', () => {
  it.each([
    ['granted', true, 'granted'],
    ['undetermined', true, 'undetermined'],
    ['denied', true, 'denied'],
    ['denied', false, 'blocked'],
  ])('expo-location %s (canAskAgain %s) is %s', (status, canAsk, expected) => expect(fromLocationStatus(status, canAsk)).toBe(expected));

  it.each([
    ['granted', 'granted'],
    ['denied', 'denied'],
    ['never_ask_again', 'blocked'],
  ])('PermissionsAndroid %s is %s', (result, expected) => expect(fromAndroidResult(result)).toBe(expected));
});

describe('what each step says and does', () => {
  it('denied: ask again with a plain note; denied forever: send the driver to Settings', () => {
    const denied = guideFor('foreground', 'denied', 35);
    expect(denied.action).toBe('request');
    expect(denied.retryNote).toBeTruthy();
    const blocked = guideFor('foreground', 'blocked', 35);
    expect(blocked.action).toBe('openSettings');
    expect(blocked.actionLabel).toBe('Open settings');
    expect(blocked.tap).toMatch(/Permissions/);
    expect(guideFor('notifications', 'blocked', 35).action).toBe('openSettings');
  });
  it('every step explains why and what to tap', () => {
    for (const step of ['foreground', 'background', 'notifications'] as const) {
      const g = guideFor(step, 'undetermined', 35);
      expect(g.why.length).toBeGreaterThan(20);
      expect(g.tap.length).toBeGreaterThan(10);
      expect(g.retryNote).toBeUndefined();
    }
  });
});
