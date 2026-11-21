import { Linking, Pressable, StyleSheet } from 'react-native';
import { env } from '../config/env';
import { colors } from '../theme/tokens';
import { Text } from './Text';

/**
 * "Privacy Policy": opens the public policy page (EXPO_PUBLIC_PRIVACY_POLICY_URL, see app.config.ts). Google Play requires a link inside the app.
 * Renders nothing when the address is not configured, so a missing variable can never break a screen.
 */
export function PrivacyPolicyLink({ align = 'center' }: { align?: 'center' | 'left' }) {
  const url = env.privacyPolicyUrl;
  if (!url) return null;
  return (
    <Pressable
      accessibilityRole="link"
      accessibilityLabel="Privacy Policy, opens in your browser"
      hitSlop={8}
      onPress={() => void Linking.openURL(url).catch(() => undefined)}
      style={styles.press}
    >
      <Text variant="small" color={colors.primaryText} weight="semibold" style={[styles.text, { textAlign: align }]}>
        Privacy Policy
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  press: { minHeight: 44, justifyContent: 'center', alignSelf: 'stretch' },
  text: { textDecorationLine: 'underline' },
});
