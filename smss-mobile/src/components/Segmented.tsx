import { Pressable, StyleSheet, View } from 'react-native';
import { colors, MIN_TOUCH, radii } from '../theme/tokens';
import { Text } from './Text';

export interface SegmentedProps<T extends string> {
  options: { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
}

/** A two-or-three way segmented control (Upcoming / Past, Pending / Decided, ...). The selected segment is filled AND bold. */
export function Segmented<T extends string>({ options, value, onChange }: SegmentedProps<T>) {
  return (
    <View style={styles.wrap} accessibilityRole="tablist">
      {options.map((o) => {
        const selected = o.value === value;
        return (
          <Pressable
            key={o.value}
            accessibilityRole="tab"
            accessibilityState={{ selected }}
            onPress={() => onChange(o.value)}
            style={[styles.segment, selected && styles.selected]}
          >
            <Text variant="label" color={selected ? colors.primaryText : colors.muted} weight={selected ? 'bold' : 'semibold'}>
              {o.label}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { flexDirection: 'row', backgroundColor: colors.divider, borderRadius: radii.input, padding: 4, gap: 4 },
  segment: { flex: 1, minHeight: MIN_TOUCH - 4, alignItems: 'center', justifyContent: 'center', borderRadius: radii.input - 4 },
  selected: { backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.tintBorder },
});
