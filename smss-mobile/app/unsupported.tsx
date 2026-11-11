import { StyleSheet } from 'react-native';
import { useSession } from '../src/auth/session';
import { Button } from '../src/components/Button';
import { Card } from '../src/components/Card';
import { EmptyState } from '../src/components/EmptyState';
import { Screen } from '../src/components/Screen';

/** Any account that is neither a Student nor a Teacher (School Admin, Principal, Accountant, ...) lands here. Those roles stay on the web. */
export default function Unsupported() {
  const { signOut, user } = useSession();
  return (
    <Screen>
      <Card style={styles.card}>
        <EmptyState
          icon="monitor"
          title="This account is not supported in the mobile app yet"
          message={`${user?.displayName ?? 'This account'} can use the SMSS website instead. The mobile app is for students and teachers.`}
        />
        <Button title="Sign out" icon="log-out" onPress={signOut} />
      </Card>
    </Screen>
  );
}

const styles = StyleSheet.create({ card: { marginTop: 48, gap: 8 } });
