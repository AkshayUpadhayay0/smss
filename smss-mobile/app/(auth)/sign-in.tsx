import { StatusBar } from 'expo-status-bar';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { messageOf } from '../../src/api/errors';
import { useSession } from '../../src/auth/session';
import { Button } from '../../src/components/Button';
import { Icon } from '../../src/components/Icon';
import { Input } from '../../src/components/Input';
import { Text } from '../../src/components/Text';
import { colors } from '../../src/theme/tokens';

export default function SignIn() {
  const { signIn } = useSession();
  const insets = useSafeAreaInsets();
  const [loginId, setLoginId] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    if (busy) return;
    if (!loginId.trim() || !password) {
      setError('Enter your username and password.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await signIn(loginId, password);
      // The root layout's guards take over from here.
    } catch (e) {
      setError(messageOf(e));
      setBusy(false);
    }
  }

  return (
    <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <StatusBar style="light" />
      <ScrollView style={styles.flex} contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled" bounces={false}>
        <View style={[styles.hero, { paddingTop: insets.top + 40 }]}>
          <View style={styles.logoTile} accessibilityElementsHidden importantForAccessibility="no">
            <Icon name="book-open" size={32} color={colors.primary} />
          </View>
          <Text variant="display" color={colors.onPrimary} accessibilityRole="header">
            Welcome back
          </Text>
          <Text color="#EDE9FE">Sign in to your school account.</Text>
        </View>

        <View style={[styles.sheet, { paddingBottom: insets.bottom + 24 }]}>
          <Input
            label="Username"
            value={loginId}
            onChangeText={setLoginId}
            autoCapitalize="none"
            autoCorrect={false}
            autoComplete="username"
            textContentType="username"
            returnKeyType="next"
            editable={!busy}
          />
          <Input
            label="Password"
            value={password}
            onChangeText={setPassword}
            password
            autoCapitalize="none"
            autoCorrect={false}
            autoComplete="current-password"
            textContentType="password"
            returnKeyType="go"
            onSubmitEditing={submit}
            editable={!busy}
          />
          {error ? (
            <Text variant="small" color={colors.danger} accessibilityLiveRegion="polite" accessibilityRole="alert">
              {error}
            </Text>
          ) : null}
          <Button title="Sign in" icon="log-in" onPress={submit} loading={busy} />
          <Text variant="small" style={styles.center}>
            Forgot your password? Ask your school office to reset it.
          </Text>
        </View>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: colors.primary },
  scroll: { flexGrow: 1 },
  hero: { backgroundColor: colors.primary, paddingHorizontal: 24, paddingBottom: 48, gap: 8, minHeight: 280 },
  logoTile: { width: 64, height: 64, borderRadius: 20, backgroundColor: colors.surface, alignItems: 'center', justifyContent: 'center', marginBottom: 16 },
  sheet: { flexGrow: 1, backgroundColor: colors.surface, borderTopLeftRadius: 28, borderTopRightRadius: 28, padding: 24, gap: 16, marginTop: -24 },
  center: { textAlign: 'center' },
});
