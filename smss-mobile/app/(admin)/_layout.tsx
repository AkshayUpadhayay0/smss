import { Stack } from 'expo-router';

// The School Admin app is one screen for now (the dashboard). Detail screens are added here as siblings later.
export default function Layout() {
  return <Stack screenOptions={{ headerShown: false }} />;
}
