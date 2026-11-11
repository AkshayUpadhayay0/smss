import { Stack } from 'expo-router';

// Tabs live in (tabs). Detail screens (no tab bar) are added here as siblings in later phases.
export default function Layout() {
  return <Stack screenOptions={{ headerShown: false }} />;
}
