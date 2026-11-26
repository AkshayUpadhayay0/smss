import { useRouter } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, StyleSheet } from 'react-native';
import { messageOf } from '../../api/errors';
import { useSession } from '../../auth/session';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Input } from '../../components/Input';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { colors } from '../../theme/tokens';
import { passwordProblem } from './rules';

/** A signed-in parent changing their own password (the first-login screen under (auth) is for temporary passwords only). */
export function FamilyChangePassword() {
  const { changePassword } = useSession();
  const router = useRouter();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function submit() {
    if (busy) return;
    const problem = passwordProblem(current, next, confirm);
    if (problem) {
      setError(problem);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await changePassword(current, next);
      setDone(true);
      setCurrent('');
      setNext('');
      setConfirm('');
    } catch (e) {
      setError(messageOf(e));
    }
    setBusy(false);
  }

  return (
    <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <Screen title="Change password" onBack={() => router.back()}>
        <Card style={styles.card}>
          <Input label="Current password" value={current} onChangeText={setCurrent} password autoCapitalize="none" autoCorrect={false} textContentType="password" editable={!busy} />
          <Input label="New password" value={next} onChangeText={setNext} password autoCapitalize="none" autoCorrect={false} textContentType="newPassword" editable={!busy} />
          <Input label="Confirm new password" value={confirm} onChangeText={setConfirm} password autoCapitalize="none" autoCorrect={false} textContentType="newPassword" returnKeyType="go" onSubmitEditing={submit} editable={!busy} />
          {error ? <Text variant="small" color={colors.danger} accessibilityLiveRegion="polite" accessibilityRole="alert">{error}</Text> : null}
          {done ? <Text variant="small" color={colors.success} accessibilityLiveRegion="polite">Your password has been changed.</Text> : null}
          <Button title="Update password" icon="lock" onPress={submit} loading={busy} />
        </Card>
      </Screen>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: colors.background },
  card: { gap: 16 },
});
