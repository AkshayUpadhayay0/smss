import { StyleSheet, View, type StyleProp, type ViewProps, type ViewStyle } from 'react-native';
import { colors, radii, shadow } from '../theme/tokens';

export function Card({ style, ...rest }: ViewProps & { style?: StyleProp<ViewStyle> }) {
  return <View {...rest} style={[styles.card, style]} />;
}

const styles = StyleSheet.create({
  card: { backgroundColor: colors.surface, borderRadius: radii.card, borderWidth: 1, borderColor: colors.border, padding: 16, ...shadow.card },
});
