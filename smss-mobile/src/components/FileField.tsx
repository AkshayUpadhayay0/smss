import * as DocumentPicker from 'expo-document-picker';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import type { PickedFile } from '../api/teacher';
import { validateAttachment } from '../features/teacher/rules';
import { colors, MIN_TOUCH, radii } from '../theme/tokens';
import { Button } from './Button';
import { Icon } from './Icon';
import { Text } from './Text';

function sizeLabel(bytes: number | null | undefined): string {
  if (!bytes && bytes !== 0) return '';
  return bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

export interface FileFieldProps {
  label: string;
  value: PickedFile | null;
  onChange: (file: PickedFile | null) => void;
  disabled?: boolean;
}

/** Picks one PDF or image (up to 5 MB). Validated here for instant feedback; the server checks the real file signature again. */
export function FileField({ label, value, onChange, disabled }: FileFieldProps) {
  const [error, setError] = useState<string | null>(null);
  const [size, setSize] = useState<number | null>(null);

  async function pick() {
    setError(null);
    try {
      const result = await DocumentPicker.getDocumentAsync({ type: ['application/pdf', 'image/*'], copyToCacheDirectory: true, multiple: false });
      if (result.canceled) return;
      const asset = result.assets[0];
      const problem = validateAttachment({ name: asset.name, size: asset.size });
      if (problem) {
        setError(problem);
        return;
      }
      setSize(asset.size ?? null);
      onChange({ uri: asset.uri, name: asset.name, mimeType: asset.mimeType ?? 'application/octet-stream' });
    } catch {
      setError('Could not open the file picker. Please try again.');
    }
  }

  return (
    <View style={{ gap: 6 }}>
      <Text variant="label">{label}</Text>
      {value ? (
        <View style={styles.chosen}>
          <Icon name="paperclip" size={18} color={colors.primaryText} />
          <View style={{ flex: 1 }}>
            <Text variant="label" color={colors.primaryText} numberOfLines={1}>
              {value.name}
            </Text>
            {size !== null ? <Text variant="small">{sizeLabel(size)}</Text> : null}
          </View>
          <Pressable onPress={() => onChange(null)} disabled={disabled} accessibilityRole="button" accessibilityLabel="Remove attachment" hitSlop={8} style={styles.remove}>
            <Icon name="x" size={20} color={colors.inkSoft} />
          </Pressable>
        </View>
      ) : (
        <Button title="Attach a file" icon="paperclip" variant="secondary" onPress={pick} disabled={disabled} />
      )}
      <Text variant="small">PDF or image (PNG, JPG, WEBP), up to 5 MB. Optional.</Text>
      {error ? (
        <Text variant="small" color={colors.danger} accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  chosen: { flexDirection: 'row', alignItems: 'center', gap: 10, minHeight: 56, paddingHorizontal: 14, borderRadius: radii.input, backgroundColor: colors.tint, borderWidth: 1, borderColor: colors.tintBorder },
  remove: { width: MIN_TOUCH, height: MIN_TOUCH, alignItems: 'center', justifyContent: 'center' },
});
