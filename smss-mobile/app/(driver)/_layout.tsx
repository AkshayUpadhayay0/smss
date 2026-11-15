import { Stack } from 'expo-router';

// Driver app: Home (index), then the one-time setup screens (disclosure, permissions, battery). Reachability is decided by the root layout's guards.
export default function Layout() {
  return <Stack screenOptions={{ headerShown: false }} />;
}
