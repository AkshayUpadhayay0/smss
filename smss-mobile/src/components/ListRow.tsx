import type { ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { colors, MIN_TOUCH } from '../theme/tokens';
import { Icon, type IconName } from './Icon';
import { Text } from './Text';

export interface ListRowProps {
  title: string;
  subtitle?: string;
  icon?: IconName;
  right?: ReactNode;
  onPress?: () => void;
  /** Shows a chevron when pressable. */
  chevron?: boolean;
}

export function ListRow({ title, subtitle, icon, right, onPress, chevron = !!onPress }: ListRowProps) {
  const content = (
    <View style={styles.row}>
      {icon ? (
        <View style={styles.iconTile}>
          <Icon name={icon} size={18} color={colors.primaryText} />
        </View>
      ) : null}
      <View style={styles.text}>
        <Text variant="heading" style={styles.title}>
          {title}
        </Text>
        {subtitle ? <Text variant="small">{subtitle}</Text> : null}
      </View>
      {right}
      {chevron ? <Icon name="chevron-right" size={20} color={colors.muted} /> : null}
    </View>
  );
  if (!onPress) return content;
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={subtitle ? `${title}, ${subtitle}` : title} onPress={onPress} style={({ pressed }) => (pressed ? styles.pressed : null)}>
      {content}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: MIN_TOUCH + 8, paddingVertical: 8 },
  iconTile: { width: 36, height: 36, borderRadius: 10, backgroundColor: colors.tint, alignItems: 'center', justifyContent: 'center' },
  text: { flex: 1, gap: 2 },
  title: { fontSize: 15 },
  pressed: { opacity: 0.7 },
});
