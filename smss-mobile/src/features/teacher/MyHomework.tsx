import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Alert, Modal, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { messageOf } from '../../api/errors';
import { deleteHomework, updateHomework, type MyHomework } from '../../api/teacher';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { DateField } from '../../components/DateField';
import { Icon } from '../../components/Icon';
import { Input } from '../../components/Input';
import { QueryState } from '../../components/QueryState';
import { Text } from '../../components/Text';
import { formatShort } from '../../lib/dates';
import { classLabel } from '../../lib/format';
import { colors, radii, spacing } from '../../theme/tokens';
import { AttachmentButton, DueChip } from '../student/parts';
import { teacherKeys, useMyHomework } from './queries';
import { HOMEWORK_DESCRIPTION_MAX, HOMEWORK_TITLE_MAX } from './rules';

function EditSheet({ hw, today, onClose }: { hw: MyHomework; today: string; onClose: () => void }) {
  const client = useQueryClient();
  const [title, setTitle] = useState(hw.title);
  const [description, setDescription] = useState(hw.description ?? '');
  const [due, setDue] = useState<string>(hw.dueDate);
  const [titleError, setTitleError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: () => updateHomework(hw.homeworkId, { title: title.trim(), description: description.trim(), assignedDate: hw.assignedDate, dueDate: due }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: teacherKeys.homework });
      onClose();
    },
  });

  function submit() {
    if (!title.trim()) {
      setTitleError('Enter a title.');
      return;
    }
    setTitleError(null);
    save.mutate();
  }

  return (
    <Modal visible animationType="slide" onRequestClose={onClose}>
      <SafeAreaView style={{ flex: 1, backgroundColor: colors.background }}>
        <ScrollView contentContainerStyle={{ padding: spacing.lg, gap: spacing.lg }} keyboardShouldPersistTaps="handled">
          <Text variant="title" accessibilityRole="header">
            Edit homework
          </Text>
          <Text variant="small">
            {hw.subjectName} · {classLabel(hw.className, hw.sectionName)}. The class, section and subject cannot be changed.
          </Text>
          <Input label="Title" value={title} onChangeText={setTitle} maxLength={HOMEWORK_TITLE_MAX} error={titleError} editable={!save.isPending} />
          <Input label="Details (optional)" value={description} onChangeText={setDescription} multiline maxLength={HOMEWORK_DESCRIPTION_MAX} showCount editable={!save.isPending} />
          <DateField label="Due date" value={due} onChange={setDue} min={hw.assignedDate} initial={today} />
          {save.isError ? (
            <View style={styles.error} accessibilityRole="alert" accessibilityLiveRegion="assertive">
              <Icon name="alert-circle" size={18} color={colors.danger} />
              <Text variant="small" color={colors.danger} style={{ flex: 1 }}>
                {messageOf(save.error)}
              </Text>
            </View>
          ) : null}
          <Button title="Save changes" icon="save" onPress={submit} loading={save.isPending} />
          <Button title="Cancel" variant="ghost" onPress={onClose} disabled={save.isPending} />
        </ScrollView>
      </SafeAreaView>
    </Modal>
  );
}

/** The homework THIS teacher published (GET /Homework/my returns only their own), so every card here may be edited or deleted. */
export function MyHomeworkList({ today }: { today: string }) {
  const client = useQueryClient();
  const homework = useMyHomework();
  const [editing, setEditing] = useState<MyHomework | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const remove = useMutation({
    mutationFn: (id: number) => deleteHomework(id),
    onSuccess: () => void client.invalidateQueries({ queryKey: teacherKeys.homework }),
    onError: (e) => setDeleteError(messageOf(e)),
  });

  function confirmDelete(hw: MyHomework) {
    Alert.alert('Delete this homework?', `"${hw.title}" will be hidden from students.`, [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete',
        style: 'destructive',
        onPress: () => {
          setDeleteError(null);
          remove.mutate(hw.homeworkId);
        },
      },
    ]);
  }

  const list = [...(homework.data ?? [])].sort((a, b) => b.dueDate.localeCompare(a.dueDate) || b.homeworkId - a.homeworkId);
  return (
    <>
      {deleteError ? (
        <View style={styles.error} accessibilityRole="alert" accessibilityLiveRegion="assertive">
          <Icon name="alert-circle" size={18} color={colors.danger} />
          <Text variant="small" color={colors.danger} style={{ flex: 1 }}>
            {deleteError}
          </Text>
        </View>
      ) : null}
      <QueryState
        loading={homework.isPending}
        error={homework.error}
        onRetry={() => void homework.refetch()}
        empty={list.length === 0 ? { title: 'No homework yet', message: 'Homework you publish will show up here.', icon: 'book-open' } : null}
      >
        {list.map((h) => (
          <Card key={h.homeworkId} style={{ gap: 8 }}>
            <View style={styles.chips}>
              <Chip label={h.subjectName} tone="primary" />
              <Chip label={classLabel(h.className, h.sectionName) ?? ''} tone="neutral" icon="users" />
              <DueChip dueDate={h.dueDate} today={today} />
            </View>
            <Text variant="heading">{h.title}</Text>
            {h.description ? <Text variant="body">{h.description}</Text> : null}
            <Text variant="small">Assigned {formatShort(h.assignedDate)}</Text>
            {h.hasAttachment ? <AttachmentButton homeworkId={h.homeworkId} fileName={h.attachmentFileName} /> : null}
            <View style={styles.actions}>
              <Button title="Edit" icon="edit-2" variant="secondary" onPress={() => setEditing(h)} style={{ flex: 1 }} />
              <Button title="Delete" icon="trash-2" variant="danger" onPress={() => confirmDelete(h)} disabled={remove.isPending} style={{ flex: 1 }} />
            </View>
          </Card>
        ))}
      </QueryState>
      {editing ? <EditSheet hw={editing} today={today} onClose={() => setEditing(null)} /> : null}
    </>
  );
}

const styles = StyleSheet.create({
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  actions: { flexDirection: 'row', gap: 10, paddingTop: 4 },
  error: { flexDirection: 'row', alignItems: 'flex-start', gap: 8, padding: 12, borderRadius: radii.input, backgroundColor: colors.dangerTint },
});
