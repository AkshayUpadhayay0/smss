import { Stack } from 'expo-router';

// Reachability is decided ONLY by the root layout's guards ((auth) is open while signed out or locked to a password change) and by app/index.tsx,
// which sends each state to the right screen. Nested Stack.Protected guards in here fought the root guards and looped when a signed-in group
// unmounted on sign-out ("Maximum update depth exceeded", found on the emulator).
export default function AuthLayout() {
  return <Stack screenOptions={{ headerShown: false }} />;
}
