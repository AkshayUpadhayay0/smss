import { Stack } from 'expo-router';
import { useSession } from '../../src/auth/session';

export default function AuthLayout() {
  const { status, needsPasswordChange } = useSession();
  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Protected guard={status === 'signedOut'}>
        <Stack.Screen name="sign-in" />
      </Stack.Protected>
      <Stack.Protected guard={needsPasswordChange}>
        <Stack.Screen name="change-password" />
      </Stack.Protected>
    </Stack>
  );
}
