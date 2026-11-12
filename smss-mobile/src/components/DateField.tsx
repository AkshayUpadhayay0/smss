import DateTimePicker, { DateTimePickerAndroid, type DateTimePickerEvent } from '@react-native-community/datetimepicker';
import { useState } from 'react';
import { Modal, Platform, Pressable, StyleSheet, View } from 'react-native';
import { formatFull, parts, ymd, type Ymd } from '../lib/dates';
import { colors, radii, spacing } from '../theme/tokens';
import { Button } from './Button';
import { Icon } from './Icon';
import { Text } from './Text';

// The native picker works in the phone's local time zone, so a Date here is only ever used as a carrier for the calendar day the user picked:
// it is built from, and read back into, a plain "yyyy-MM-dd" string with local year/month/day. The default and the limits come from the SERVER's date.
const toDate = (value: Ymd) => {
  const p = parts(value);
  return new Date(p.year, p.month - 1, p.day);
};
const fromDate = (d: Date): Ymd => ymd(d.getFullYear(), d.getMonth() + 1, d.getDate());

export interface DateFieldProps {
  label: string;
  value: Ymd | null;
  onChange: (value: Ymd) => void;
  min?: Ymd;
  max?: Ymd;
  /** Where the picker opens when there is no value yet (pass the server's today). */
  initial: Ymd;
  error?: string | null;
  hint?: string;
}

export function DateField({ label, value, onChange, min, max, initial, error, hint }: DateFieldProps) {
  const [iosOpen, setIosOpen] = useState(false);
  const [draft, setDraft] = useState<Ymd>(value ?? initial);

  function open() {
    const start = value ?? initial;
    if (Platform.OS === 'android') {
      DateTimePickerAndroid.open({
        value: toDate(start),
        mode: 'date',
        minimumDate: min ? toDate(min) : undefined,
        maximumDate: max ? toDate(max) : undefined,
        onChange: (event: DateTimePickerEvent, date?: Date) => {
          if (event.type === 'set' && date) onChange(fromDate(date));
        },
      });
    } else {
      setDraft(start);
      setIosOpen(true);
    }
  }

  return (
    <View style={{ gap: 6 }}>
      <Text variant="label">{label}</Text>
      <Pressable
        onPress={open}
        accessibilityRole="button"
        accessibilityLabel={`${label}: ${value ? formatFull(value) : 'not set'}`}
        style={[styles.field, !!error && { borderColor: colors.danger }]}
      >
        <Icon name="calendar" size={18} color={colors.primaryText} />
        <Text variant="body" color={value ? colors.ink : colors.muted} style={{ flex: 1 }}>
          {value ? formatFull(value) : 'Select a date'}
        </Text>
      </Pressable>
      {error ? (
        <Text variant="small" color={colors.danger} accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : hint ? (
        <Text variant="small">{hint}</Text>
      ) : null}

      {Platform.OS === 'ios' ? (
        <Modal visible={iosOpen} transparent animationType="slide" onRequestClose={() => setIosOpen(false)}>
          <View style={styles.root}>
            <Pressable style={styles.backdrop} onPress={() => setIosOpen(false)} />
            <View style={styles.sheet}>
              <DateTimePicker
                value={toDate(draft)}
                mode="date"
                display="inline"
                minimumDate={min ? toDate(min) : undefined}
                maximumDate={max ? toDate(max) : undefined}
                onChange={(_e, d) => d && setDraft(fromDate(d))}
              />
              <Button
                title="Done"
                onPress={() => {
                  onChange(draft);
                  setIosOpen(false);
                }}
              />
            </View>
          </View>
        </Modal>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  field: { flexDirection: 'row', alignItems: 'center', gap: 10, minHeight: 50, paddingHorizontal: 14, borderRadius: radii.input, borderWidth: 1.5, borderColor: colors.inputBorder, backgroundColor: colors.surface },
  root: { flex: 1, justifyContent: 'flex-end' },
  backdrop: { ...StyleSheet.absoluteFill, backgroundColor: 'rgba(30,27,46,0.45)' },
  sheet: { backgroundColor: colors.surface, borderTopLeftRadius: 28, borderTopRightRadius: 28, padding: spacing.lg, gap: spacing.md },
});
