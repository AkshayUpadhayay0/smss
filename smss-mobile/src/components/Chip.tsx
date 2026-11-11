import { StyleSheet, View } from 'react-native';
import { colors, radii } from '../theme/tokens';
import { Icon, type IconName } from './Icon';
import { Text } from './Text';

export type ChipTone = 'neutral' | 'primary' | 'success' | 'danger' | 'warning';

const tones: Record<ChipTone, { bg: string; fg: string; border: string }> = {
  neutral: { bg: colors.divider, fg: colors.inkSoft, border: colors.border },
  primary: { bg: colors.tint, fg: colors.primaryText, border: colors.tintBorder },
  success: { bg: colors.successTint, fg: colors.success, border: colors.successBorder },
  danger: { bg: colors.dangerTint, fg: colors.danger, border: colors.dangerTint },
  warning: { bg: colors.warningTint, fg: colors.warningText, border: colors.warningTint },
};

/** Status is always text (plus an optional icon), never colour alone. */
export function Chip({ label, tone = 'neutral', icon }: { label: string; tone?: ChipTone; icon?: IconName }) {
  const t = tones[tone];
  return (
    <View style={[styles.chip, { backgroundColor: t.bg, borderColor: t.border }]}>
      {icon ? <Icon name={icon} size={13} color={t.fg} /> : null}
      <Text variant="small" color={t.fg} weight="semibold">
        {label}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  chip: { flexDirection: 'row', alignItems: 'center', gap: 4, alignSelf: 'flex-start', borderRadius: radii.chip, borderWidth: 1, paddingHorizontal: 10, paddingVertical: 4 },
});
