import { useState } from 'react';
import { FlatList, Modal, Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, MIN_TOUCH, radii, spacing } from '../theme/tokens';
import { Icon } from './Icon';
import { Text } from './Text';

export interface SelectOption<T extends string | number> {
  value: T;
  label: string;
}

export interface SelectProps<T extends string | number> {
  label: string;
  value: T | null;
  options: SelectOption<T>[];
  onChange: (value: T) => void;
  placeholder?: string;
  disabled?: boolean;
  error?: string | null;
  /** Shown in the sheet when there are no options. */
  emptyText?: string;
}

/** A labelled field that opens a bottom sheet of options. */
export function Select<T extends string | number>({ label, value, options, onChange, placeholder = 'Select', disabled, error, emptyText = 'Nothing to choose from.' }: SelectProps<T>) {
  const [open, setOpen] = useState(false);
  const insets = useSafeAreaInsets();
  const selected = options.find((o) => o.value === value);
  const inactive = disabled || options.length === 0;

  return (
    <View style={{ gap: 6 }}>
      <Text variant="label">{label}</Text>
      <Pressable
        onPress={() => setOpen(true)}
        disabled={inactive}
        accessibilityRole="button"
        accessibilityLabel={`${label}: ${selected?.label ?? placeholder}`}
        accessibilityState={{ disabled: !!inactive }}
        style={[styles.field, !!error && styles.errored, inactive && { opacity: 0.55 }]}
      >
        <Text variant="body" color={selected ? colors.ink : colors.muted} style={{ flex: 1 }} numberOfLines={1}>
          {selected?.label ?? (options.length === 0 && !disabled ? emptyText : placeholder)}
        </Text>
        <Icon name="chevron-down" size={20} color={colors.muted} />
      </Pressable>
      {error ? (
        <Text variant="small" color={colors.danger} accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : null}

      <Modal visible={open} transparent animationType="slide" onRequestClose={() => setOpen(false)} statusBarTranslucent>
        <View style={styles.root}>
          <Pressable style={styles.backdrop} onPress={() => setOpen(false)} accessibilityRole="button" accessibilityLabel="Close" />
          <View style={[styles.sheet, { paddingBottom: insets.bottom + spacing.md }]} accessibilityViewIsModal>
            <Text variant="heading" accessibilityRole="header" style={{ paddingHorizontal: spacing.lg, paddingBottom: spacing.sm }}>
              {label}
            </Text>
            <FlatList
              data={options}
              keyExtractor={(o) => String(o.value)}
              style={{ maxHeight: 420 }}
              ListEmptyComponent={
                <Text variant="small" style={{ padding: spacing.lg }}>
                  {emptyText}
                </Text>
              }
              renderItem={({ item }) => {
                const isSel = item.value === value;
                return (
                  <Pressable
                    onPress={() => {
                      onChange(item.value);
                      setOpen(false);
                    }}
                    accessibilityRole="button"
                    accessibilityState={{ selected: isSel }}
                    accessibilityLabel={item.label}
                    style={[styles.option, isSel && { backgroundColor: colors.tint }]}
                  >
                    <Text variant="body" color={colors.ink} weight={isSel ? 'bold' : 'regular'} style={{ flex: 1 }}>
                      {item.label}
                    </Text>
                    {isSel ? <Icon name="check" size={20} color={colors.primaryText} /> : null}
                  </Pressable>
                );
              }}
            />
          </View>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  field: { flexDirection: 'row', alignItems: 'center', gap: 8, minHeight: 50, paddingHorizontal: 14, borderRadius: radii.input, borderWidth: 1.5, borderColor: colors.inputBorder, backgroundColor: colors.surface },
  errored: { borderColor: colors.danger },
  root: { flex: 1, justifyContent: 'flex-end' },
  backdrop: { ...StyleSheet.absoluteFill, backgroundColor: 'rgba(30,27,46,0.45)' },
  sheet: { backgroundColor: colors.surface, borderTopLeftRadius: 28, borderTopRightRadius: 28, paddingTop: spacing.lg },
  option: { flexDirection: 'row', alignItems: 'center', minHeight: MIN_TOUCH + 4, paddingHorizontal: spacing.lg },
});
