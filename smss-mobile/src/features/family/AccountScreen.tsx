import { useRouter, type Href } from 'expo-router';
import { StyleSheet } from 'react-native';
import { useSession } from '../../auth/session';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { PrivacyPolicyLink } from '../../components/PrivacyPolicyLink';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { colors } from '../../theme/tokens';

/** The parent's account: who they are (name, login id, school), change password, sign out. */
export function FamilyAccount() {
  const { user, signOut } = useSession();
  const router = useRouter();
  return (
    <Screen title="Your account">
      <Card style={styles.card}>
        <Text variant="small">Name</Text>
        <Text weight="bold" color={colors.ink}>{user?.displayName ?? '—'}</Text>
        <Text variant="small">Your login ID</Text>
        <Text weight="bold" color={colors.ink}>{user?.orgUserId ?? '—'}</Text>
        <Text variant="small">School</Text>
        <Text weight="bold" color={colors.ink}>{user?.schoolName ?? '—'}</Text>
      </Card>
      <Button title="Change password" icon="lock" variant="secondary" onPress={() => router.push('/(family)/change-password' as Href)} />
      <Button title="Sign out" icon="log-out" onPress={() => void signOut()} />
      <PrivacyPolicyLink />
    </Screen>
  );
}

const styles = StyleSheet.create({ card: { gap: 4 } });
