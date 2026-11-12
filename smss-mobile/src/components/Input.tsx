import { useState } from 'react';
import { Pressable, StyleSheet, TextInput, View, type TextInputProps } from 'react-native';
import { colors, fonts, MIN_TOUCH, radii } from '../theme/tokens';
import { Icon } from './Icon';
import { Text } from './Text';

export interface InputProps extends Omit<TextInputProps, 'style'> {
  label: string;
  error?: string | null;
  /** Adds the show/hide toggle and hides the text. */
  password?: boolean;
  /** Shows "n / maxLength" under the field (use with maxLength). */
  showCount?: boolean;
  /** Helper text under the field (replaced by the error when there is one). */
  hint?: string;
}

export function Input({ label, error, password, showCount, hint, ...rest }: InputProps) {
  const [hidden, setHidden] = useState(true);
  const [focused, setFocused] = useState(false);
  return (
    <View style={styles.wrap}>
      <Text variant="label">{label}</Text>
      <View style={[styles.field, focused && styles.focused, !!error && styles.errored]}>
        <TextInput
          {...rest}
          accessibilityLabel={label}
          secureTextEntry={password ? hidden : rest.secureTextEntry}
          placeholderTextColor={colors.muted}
          onFocus={(e) => {
            setFocused(true);
            rest.onFocus?.(e);
          }}
          onBlur={(e) => {
            setFocused(false);
            rest.onBlur?.(e);
          }}
          style={[styles.input, rest.multiline && styles.multiline]}
        />
        {password ? (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={hidden ? 'Show password' : 'Hide password'}
            onPress={() => setHidden((h) => !h)}
            hitSlop={8}
            style={styles.toggle}
          >
            <Icon name={hidden ? 'eye' : 'eye-off'} size={20} color={colors.muted} />
          </Pressable>
        ) : null}
      </View>
      {error ? (
        <Text variant="small" color={colors.danger} accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : hint ? (
        <Text variant="small">{hint}</Text>
      ) : null}
      {showCount && rest.maxLength ? (
        <Text variant="small" style={{ textAlign: 'right' }}>
          {(rest.value ?? '').length} / {rest.maxLength}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: 6 },
  field: { flexDirection: 'row', alignItems: 'center', minHeight: 50, borderRadius: radii.input, borderWidth: 1.5, borderColor: colors.inputBorder, backgroundColor: colors.surface },
  focused: { borderColor: colors.primary },
  errored: { borderColor: colors.danger },
  input: { flex: 1, paddingHorizontal: 14, paddingVertical: 12, fontFamily: fonts.regular, fontSize: 16, color: colors.ink },
  multiline: { minHeight: 96, textAlignVertical: 'top' },
  toggle: { minWidth: MIN_TOUCH, minHeight: MIN_TOUCH, alignItems: 'center', justifyContent: 'center' },
});
