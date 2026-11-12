import { Redirect } from 'expo-router';
import { ActivityIndicator, View } from 'react-native';
import { useSession } from '../src/auth/session';
import { colors } from '../src/theme/tokens';

// A neutral, always-reachable screen. Sign-out navigates here FIRST, so the signed-in route group is unmounted while a valid screen is
// active (removing a whole group under the user's feet made expo-router loop: "Maximum update depth exceeded", found on the emulator).
// Once the session has ended, hand over to the anchor route, which sends a signed-out user to sign-in.
export default function SigningOut() {
  const { status } = useSession();
  if (status === 'signedOut') return <Redirect href="/" />;
  return (
    <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.background }}>
      <ActivityIndicator color={colors.primary} />
    </View>
  );
}
