import type { ExpoConfig } from 'expo/config';

// The API base URL is configuration, never a literal in source. Local dev: .env (untracked). EAS builds: EAS environment variables.
// The release-build HTTPS rule is enforced at runtime in src/config/env.ts.
// !!! IDENTIFIERS ARE PERMANENT !!! `com.smsserp.mobile` (ios.bundleIdentifier / android.package) is still awaiting the owner's final confirmation. After the first
// store build it can never change. It must be confirmed by the owner BEFORE any store or production build. Do not build for stores until then.
// The Firebase app (project smss-erp, google-services.json) is registered against com.smsserp.mobile: changing the identifier means registering a new Firebase app too.
// iOS push still needs GoogleService-Info.plist (client config) and `ios.googleServicesFile: './GoogleService-Info.plist'` below: add both when the iOS Firebase app is registered (not done yet).
const config: ExpoConfig = {
  name: 'SMSS',
  slug: 'smss-mobile',
  scheme: 'smss',
  version: '1.0.0',
  orientation: 'portrait',
  userInterfaceStyle: 'light',
  icon: './assets/icon.png',
  ios: {
    supportsTablet: false,
    bundleIdentifier: 'com.smsserp.mobile',
  },
  android: {
    package: 'com.smsserp.mobile', 
    versionCode: 1,
    googleServicesFile: './google-services.json',
    permissions: [
      'ACCESS_COARSE_LOCATION',
      'ACCESS_FINE_LOCATION',
      'ACCESS_BACKGROUND_LOCATION',
      'FOREGROUND_SERVICE',
      'FOREGROUND_SERVICE_LOCATION',
      'POST_NOTIFICATIONS',
    ],
    adaptiveIcon: {
      foregroundImage: './assets/adaptive-icon.png',
      backgroundColor: '#0044ff',
    },
  },
  plugins: [
    'expo-router',
    'expo-secure-store',
    'expo-font',
    'expo-sharing',
    // Driver duty: location while the app is in the background, shown by a foreground-service notification. Needs a development / EAS build: Expo Go cannot do it.
    'expo-dev-client',
    // Push notifications (FCM). Native modules: a development build is required, Expo Go cannot run them.
    '@react-native-firebase/app',
    '@react-native-firebase/messaging',
    // React Native Firebase on iOS needs dynamic frameworks (per its Expo setup docs). No effect on Android.
    ['expo-build-properties', { ios: { useFrameworks: 'dynamic' } }],
    // Windows only, opt-in: SMSS_ANDROID_CMAKE_VERSION=4.1.2 pins the CMake version so the first Android build does not fail on 260-character paths (see CLAUDE.md).
    ...(process.env.SMSS_ANDROID_CMAKE_VERSION ? ([['./plugins/withAndroidCmakeVersion', { version: process.env.SMSS_ANDROID_CMAKE_VERSION }]] as [string, unknown][]) : []),
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
    // The public Privacy Policy page (opened from the app: sign-in, driver disclosure, profile). Google Play requires the link inside the app.
    privacyPolicyUrl: process.env.EXPO_PUBLIC_PRIVACY_POLICY_URL ?? '',
    dutyIntervalSeconds: process.env.EXPO_PUBLIC_DUTY_INTERVAL_SECONDS ?? '',
    dutyAccuracy: process.env.EXPO_PUBLIC_DUTY_ACCURACY ?? '',
    eas: {
      projectId: 'cf55cb32-354d-4434-8038-7384ad31a2c1',
    },
  },
};

export default config;
