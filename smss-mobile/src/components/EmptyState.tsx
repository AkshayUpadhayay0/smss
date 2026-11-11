import { StyleSheet, View } from 'react-native';
import { colors } from '../theme/tokens';
import { Icon, type IconName } from './Icon';
import { Text } from './Text';

export function EmptyState({ title, message, icon = 'inbox' }: { title: string; message?: string; icon?: IconName }) {
  return (
    <View style={styles.wrap} accessible accessibilityLabel={message ? `${title}. ${message}` : title}>
      <View style={styles.badge}>
        <Icon name={icon} size={26} color={colors.primaryText} />
      </View>
      <Text variant="heading" style={styles.center}>
        {title}
      </Text>
      {message ? (
        <Text variant="small" style={styles.center}>
          {message}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { alignItems: 'center', gap: 8, paddingVertical: 40, paddingHorizontal: 24 },
  badge: { width: 56, height: 56, borderRadius: 18, backgroundColor: colors.tint, alignItems: 'center', justifyContent: 'center', marginBottom: 4 },
  center: { textAlign: 'center' },
});
