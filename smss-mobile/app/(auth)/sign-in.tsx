import { Redirect } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { messageOf } from '../../src/api/errors';
import { useSession } from '../../src/auth/session';
import { Button } from '../../src/components/Button';
import { Input } from '../../src/components/Input';
import { Text } from '../../src/components/Text';
import { colors, fonts } from '../../src/theme/tokens';

export default function SignIn() {
  const { signIn, status } = useSession();
  const insets = useSafeAreaInsets();
  const [loginId, setLoginId] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Once signed in, this screen no longer applies: hand over to the anchor route, which sends the user to the right place (change password / their app).
  if (status !== 'signedOut' && status !== 'loading') return <Redirect href="/" />;

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
            <Text style={styles.logoLetter}>S</Text>
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
  flex: { flex: 1, backgroundColor: colors.primaryDeep },
  scroll: { flexGrow: 1 },
  hero: { backgroundColor: colors.primaryDeep, paddingHorizontal: 24, paddingBottom: 48, gap: 8, minHeight: 280 },
  // the design's logo tile: 56px white, radius 16, bold violet S (26px)
  logoTile: { width: 56, height: 56, borderRadius: 16, backgroundColor: colors.surface, alignItems: 'center', justifyContent: 'center', marginBottom: 16 },
  logoLetter: { fontFamily: fonts.bold, fontSize: 26, lineHeight: 32, color: colors.primaryText },
  sheet: { flexGrow: 1, backgroundColor: colors.surface, borderTopLeftRadius: 28, borderTopRightRadius: 28, padding: 24, gap: 16, marginTop: -24 },
  center: { textAlign: 'center' },
});
