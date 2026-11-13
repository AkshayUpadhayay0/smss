import type { ExpoConfig } from 'expo/config';

// The API base URL is configuration, never a literal in source. Local dev: .env (untracked). EAS builds: EAS environment variables.
// The release-build HTTPS rule is enforced at runtime in src/config/env.ts.
// !!! IDENTIFIERS ARE PERMANENT !!! `com.smss.mobile` (ios.bundleIdentifier / android.package) is a DEVELOPMENT PLACEHOLDER. After the first
// store build it can never change. It must be decided and confirmed by the owner BEFORE any store or production build. Do not build for stores until then.
const config: ExpoConfig = {
  name: 'SMSS',
  slug: 'smss-mobile',
  scheme: 'smss',
  version: '0.1.0',
  orientation: 'portrait',
  userInterfaceStyle: 'light',
  icon: './assets/icon.png',
  ios: {
    supportsTablet: false,
    bundleIdentifier: 'com.smss.mobile', // TODO confirm before the first store build: it cannot change afterwards
  },
  android: {
    package: 'com.smss.mobile', // TODO confirm before the first store build: it cannot change afterwards
    // Driver live location (Stage 2). ACCESS_FINE/COARSE are also added by the expo-location plugin; they are listed here so the full set is visible in one place.
    // FOREGROUND_SERVICE_LOCATION is required on Android 14+ for a location foreground service. We never request REQUEST_IGNORE_BATTERY_OPTIMIZATIONS (Play policy).
    permissions: [
      'ACCESS_COARSE_LOCATION',
      'ACCESS_FINE_LOCATION',
      'ACCESS_BACKGROUND_LOCATION',
      'FOREGROUND_SERVICE',
      'FOREGROUND_SERVICE_LOCATION',
      'POST_NOTIFICATIONS',
    ],
    adaptiveIcon: {
      backgroundColor: '#EDE9FE',
      foregroundImage: './assets/android-icon-foreground.png',
      backgroundImage: './assets/android-icon-background.png',
      monochromeImage: './assets/android-icon-monochrome.png',
    },
  },
  plugins: [
    'expo-router',
    'expo-secure-store',
    'expo-font',
    'expo-sharing',
    // Driver duty: location while the app is in the background, shown by a foreground-service notification. Needs a development / EAS build: Expo Go cannot do it.
    'expo-dev-client',
    [
      'expo-location',
      {
        isAndroidBackgroundLocationEnabled: true,
        isAndroidForegroundServiceEnabled: true,
        isIosBackgroundLocationEnabled: true,
        androidForegroundServiceIcon: './assets/notification-icon.png',
        locationWhenInUsePermission: "SMSS uses your location while you are on duty so your school can see where this vehicle is.",
        locationAlwaysAndWhenInUsePermission: "SMSS shares this vehicle's location with your school only while you are on duty, including when the app is in the background.",
      },
    ],
  ],
  experiments: { typedRoutes: true },
  extra: {
    apiBaseUrl: process.env.EXPO_PUBLIC_API_BASE_URL ?? '',
    // DEV BUILDS ONLY (ignored in release builds): send a fix every N seconds instead of the server's 60, for quick emulator passes.
    dutyIntervalSeconds: process.env.EXPO_PUBLIC_DUTY_INTERVAL_SECONDS ?? '',
    // DEV BUILDS ONLY: 'high' asks for GPS-grade fixes. The Android emulator has no network location, so Balanced (wifi / cell) never produces a fix there.
    dutyAccuracy: process.env.EXPO_PUBLIC_DUTY_ACCURACY ?? '',
  },
};

export default config;
