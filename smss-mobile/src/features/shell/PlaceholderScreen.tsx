import { useSession } from '../../auth/session';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { EmptyState } from '../../components/EmptyState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';

/**
 * Phase 1 tab shell: no data, no invented content. Replaced screen by screen in Phases 2 and 3.
 * `showSession` is TEMPORARY on the two Home shells so the sign-in -> role routing -> sign-out loop can be tested before the real Home exists.
 */
export function PlaceholderScreen({ title, showSession = false }: { title: string; showSession?: boolean }) {
  const { user, role, signOut } = useSession();
  return (
    <Screen title={title}>
      {showSession ? (
        <Card style={{ gap: 12 }}>
          <Text variant="heading">Hello, {user?.displayName}</Text>
          <Text variant="small">
            {user?.schoolName ?? 'No school'} · signed in as {role === 'STUDENT' ? 'a student' : 'a teacher'}
          </Text>
          <Button title="Sign out" variant="secondary" icon="log-out" onPress={signOut} />
        </Card>
      ) : null}
      <EmptyState icon="clock" title="Coming soon" message="This screen is not built yet." />
    </Screen>
  );
}
