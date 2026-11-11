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
    adaptiveIcon: {
      backgroundColor: '#EDE9FE',
      foregroundImage: './assets/android-icon-foreground.png',
      backgroundImage: './assets/android-icon-background.png',
      monochromeImage: './assets/android-icon-monochrome.png',
    },
  },
  plugins: ['expo-router', 'expo-secure-store', 'expo-font', 'expo-sharing'],
  experiments: { typedRoutes: true },
  extra: {
    apiBaseUrl: process.env.EXPO_PUBLIC_API_BASE_URL ?? '',
  },
};

export default config;
