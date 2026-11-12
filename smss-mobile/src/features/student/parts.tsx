import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { messageOf } from '../../api/errors';
import { Chip, type ChipTone } from '../../components/Chip';
import { Icon, type IconName } from '../../components/Icon';
import { Text } from '../../components/Text';
import { diffDays, formatShort } from '../../lib/dates';
import { openHomeworkAttachment } from '../../lib/attachments';
import { colors, MIN_TOUCH, radii } from '../../theme/tokens';

/** "Due today" and "Due tomorrow" are the warnings; every other date (including overdue items in the Past list) is neutral. `today` is the SERVER's date. */
export function DueChip({ dueDate, today }: { dueDate: string; today: string }) {
  const diff = diffDays(dueDate, today);
  if (diff === 1) return <Chip label="Due tomorrow" tone="warning" icon="clock" />;
  if (diff === 0) return <Chip label="Due today" tone="warning" icon="clock" />;
  return <Chip label={`Due ${formatShort(dueDate)}`} tone="neutral" icon="calendar" />;
}

const leaveTones: Record<string, { tone: ChipTone; icon: IconName }> = {
  pending: { tone: 'warning', icon: 'clock' },
  approved: { tone: 'success', icon: 'check' },
  rejected: { tone: 'danger', icon: 'x' },
  cancelled: { tone: 'neutral', icon: 'slash' },
};

/** The server's status text, with an icon so the state is never colour alone. */
export function LeaveStatusChip({ status }: { status: string }) {
  const t = leaveTones[status.trim().toLowerCase()] ?? { tone: 'neutral' as ChipTone, icon: 'info' as IconName };
  return <Chip label={status} tone={t.tone} icon={t.icon} />;
}

export function SectionHeader({ title, actionLabel, onAction }: { title: string; actionLabel?: string; onAction?: () => void }) {
  return (
    <View style={styles.sectionHeader}>
      <Text variant="heading" accessibilityRole="header">
        {title}
      </Text>
      {actionLabel && onAction ? (
        <Pressable onPress={onAction} accessibilityRole="button" accessibilityLabel={actionLabel} hitSlop={8} style={styles.action}>
          <Text variant="label" color={colors.primaryText}>
            {actionLabel}
          </Text>
        </Pressable>
      ) : null}
    </View>
  );
}

/** Downloads the homework attachment with an authenticated request, then opens it. Shows a friendly error inline. */
export function AttachmentButton({ homeworkId, fileName }: { homeworkId: number; fileName: string | null }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function open() {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      await openHomeworkAttachment(homeworkId, fileName);
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <View style={{ gap: 4 }}>
      <Pressable
        onPress={open}
        disabled={busy}
        accessibilityRole="button"
        accessibilityLabel={`Open attachment ${fileName ?? ''}`.trim()}
        accessibilityState={{ busy, disabled: busy }}
        style={[styles.attachment, busy && { opacity: 0.6 }]}
      >
        <Icon name={busy ? 'loader' : 'paperclip'} size={16} color={colors.primaryText} />
        <Text variant="label" color={colors.primaryText} numberOfLines={1} style={{ flexShrink: 1 }}>
          {busy ? 'Downloading…' : (fileName ?? 'Attachment')}
        </Text>
      </Pressable>
      {error ? (
        <Text variant="small" color={colors.danger} accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  sectionHeader: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  action: { minHeight: MIN_TOUCH - 8, justifyContent: 'center' },
  attachment: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    alignSelf: 'flex-start',
    minHeight: MIN_TOUCH,
    maxWidth: '100%',
    paddingHorizontal: 14,
    borderRadius: radii.chip,
    backgroundColor: colors.tint,
    borderWidth: 1,
    borderColor: colors.tintBorder,
  },
});
