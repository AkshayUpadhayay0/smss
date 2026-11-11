import { StyleSheet, View } from 'react-native';
import { initials } from '../lib/format';
import { colors } from '../theme/tokens';
import { Text } from './Text';

/** Initials only in v1: student and employee photos are private and are not loaded. */
export function Avatar({ name, size = 44 }: { name: string | null | undefined; size?: number }) {
  return (
    <View style={[styles.circle, { width: size, height: size, borderRadius: size / 2 }]} accessibilityElementsHidden importantForAccessibility="no">
      <Text weight="bold" color={colors.primaryText} style={{ fontSize: size * 0.38, lineHeight: size * 0.5 }}>
        {initials(name)}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  circle: { backgroundColor: colors.tint, borderWidth: 1, borderColor: colors.tintBorder, alignItems: 'center', justifyContent: 'center' },
});
