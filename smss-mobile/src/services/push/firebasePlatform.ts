import Constants from 'expo-constants';
import { PermissionsAndroid, Platform } from 'react-native';
import type { PushMessage, PushPlatform } from './pushCore';

// The real platform: React Native Firebase Messaging. It needs a development build (Expo Go does not contain it). The module is loaded lazily
// so a missing native module only makes push a silent no-op instead of breaking app start.
type Messaging = typeof import('@react-native-firebase/messaging');
let cached: { mod: Messaging; instance: ReturnType<Messaging['getMessaging']> } | null = null;

function messaging() {
  if (!cached) {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const mod = require('@react-native-firebase/messaging') as Messaging;
    cached = { mod, instance: mod.getMessaging() };
  }
  return cached;
}

const androidApi = Platform.OS === 'android' ? (typeof Platform.Version === 'number' ? Platform.Version : parseInt(String(Platform.Version), 10) || 0) : 0;

export const firebasePlatform: PushPlatform = {
  os: Platform.OS === 'ios' ? 'IOS' : 'ANDROID',
  deviceLabel: `${Platform.OS === 'ios' ? 'iOS' : 'Android'} ${Platform.Version}`,
  appVersion: Constants.expoConfig?.version,

  async requestPermission() {
    if (Platform.OS === 'android') {
      // The library's own requestPermission is a no-op on Android, so the Android 13+ runtime prompt is asked here.
      if (androidApi < 33) return true;
      return (await PermissionsAndroid.request('android.permission.POST_NOTIFICATIONS')) === PermissionsAndroid.RESULTS.GRANTED;
    }
    const { mod, instance } = messaging();
    const status = await mod.requestPermission(instance);
    return status === mod.AuthorizationStatus.AUTHORIZED || status === mod.AuthorizationStatus.PROVISIONAL;
  },

  async getToken() {
    const { mod, instance } = messaging();
    return (await mod.getToken(instance)) || null;
  },

  onTokenRefresh(listener) {
    const { mod, instance } = messaging();
    return mod.onTokenRefresh(instance, listener);
  },

  onForegroundMessage(listener) {
    const { mod, instance } = messaging();
    return mod.onMessage(instance, (m): void => {
      const message: PushMessage = { title: m.notification?.title ?? null, body: m.notification?.body ?? null };
      listener(message);
    });
  },
};
