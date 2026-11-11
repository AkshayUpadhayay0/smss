import { useState } from 'react';
import { KeyboardAvoidingView, Platform, StyleSheet, View } from 'react-native';
import { messageOf } from '../../src/api/errors';
import { useSession } from '../../src/auth/session';
import { Button } from '../../src/components/Button';
import { Card } from '../../src/components/Card';
import { Input } from '../../src/components/Input';
import { Screen } from '../../src/components/Screen';
import { Text } from '../../src/components/Text';
import { colors } from '../../src/theme/tokens';

const MIN_LENGTH = 8; // the API's rule (ChangePasswordRequest)

/** The first-login screen. While it is up, the root layout lets nothing else open (and the server 403s every other call). */
export default function ChangePassword() {
  const { changePassword, signOut } = useSession();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function validate(): string | null {
    if (!current) return 'Enter your temporary password.';
    if (next.length < MIN_LENGTH) return `The new password must be at least ${MIN_LENGTH} characters.`;
    if (next === current) return 'The new password must be different from the temporary one.';
    if (next !== confirm) return 'The new passwords do not match.';
    return null;
  }

  async function submit() {
    if (busy) return;
    const problem = validate();
    if (problem) {
      setError(problem);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await changePassword(current, next);
      // The server issued a fresh session; the guards now open the app for this user's role.
    } catch (e) {
      setError(messageOf(e));
      setBusy(false);
    }
  }

  return (
    <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <Screen title="Set a new password" subtitle="You are using a temporary password. Choose your own to continue.">
        <Card style={styles.card}>
          <Input label="Temporary password" value={current} onChangeText={setCurrent} password autoCapitalize="none" autoCorrect={false} textContentType="password" editable={!busy} />
          <Input label="New password" value={next} onChangeText={setNext} password autoCapitalize="none" autoCorrect={false} textContentType="newPassword" editable={!busy} />
          <Input
            label="Confirm new password"
            value={confirm}
            onChangeText={setConfirm}
            password
            autoCapitalize="none"
            autoCorrect={false}
            textContentType="newPassword"
            returnKeyType="go"
            onSubmitEditing={submit}
            editable={!busy}
          />
          {error ? (
            <Text variant="small" color={colors.danger} accessibilityLiveRegion="polite" accessibilityRole="alert">
              {error}
            </Text>
          ) : null}
          <Button title="Update password" icon="lock" onPress={submit} loading={busy} />
        </Card>
        <View style={styles.signOut}>
          <Button title="Sign out" variant="ghost" icon="log-out" onPress={signOut} disabled={busy} />
        </View>
      </Screen>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: colors.background },
  card: { gap: 16 },
  signOut: { alignItems: 'center' },
});
