import { Stack } from 'expo-router';

// The parent app. Reachable only while the root layout's guard says the role is GUARDIAN (fail closed). Tabs live in (tabs); a child's dashboard and change-password have no tab bar.
export default function Layout() {
  return <Stack screenOptions={{ headerShown: false }} />;
}
