import { StyleSheet, View } from 'react-native';
import { colors } from '../theme/tokens';
import { Button } from './Button';
import { Icon } from './Icon';
import { Text } from './Text';

/** Pass a message that is already safe to show (see messageOf in src/api/errors.ts). */
export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <View style={styles.wrap}>
      <View style={styles.badge}>
        <Icon name="alert-circle" size={26} color={colors.danger} />
      </View>
      <Text variant="heading" style={styles.center}>
        Something went wrong
      </Text>
      <Text variant="small" style={styles.center} accessibilityLiveRegion="polite">
        {message}
      </Text>
      {onRetry ? <Button title="Retry" icon="refresh-cw" variant="secondary" onPress={onRetry} style={styles.button} /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { alignItems: 'center', gap: 8, paddingVertical: 40, paddingHorizontal: 24 },
  badge: { width: 56, height: 56, borderRadius: 18, backgroundColor: colors.dangerTint, alignItems: 'center', justifyContent: 'center', marginBottom: 4 },
  center: { textAlign: 'center' },
  button: { marginTop: 8, minWidth: 140 },
});
