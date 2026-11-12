import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { messageOf } from '../../api/errors';
import { publishHomework, uploadHomeworkAttachment, type MyHomework, type PickedFile } from '../../api/teacher';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { DateField } from '../../components/DateField';
import { FileField } from '../../components/FileField';
import { Icon } from '../../components/Icon';
import { Input } from '../../components/Input';
import { Select } from '../../components/Select';
import { Text } from '../../components/Text';
import { diffDays } from '../../lib/dates';
import { colors, radii } from '../../theme/tokens';
import { teachingOptions } from './derive';
import { teacherKeys, useMyTimetable } from './queries';
import { HOMEWORK_DESCRIPTION_MAX, HOMEWORK_TITLE_MAX } from './rules';
import { QueryState } from '../../components/QueryState';

type Stage =
  | { kind: 'form' }
  // The homework WAS published, but its attachment did not upload: keep what is needed to retry just the upload.
  | { kind: 'attachmentFailed'; created: MyHomework; file: PickedFile; message: string }
  | { kind: 'done'; title: string; note: string | null };

interface Errors {
  section?: string;
  subject?: string;
  title?: string;
  due?: string;
}

function Banner({ tone, children }: { tone: 'danger' | 'warning'; children: string }) {
  const fg = tone === 'danger' ? colors.danger : colors.warningText;
  return (
    <View style={[styles.banner, { backgroundColor: tone === 'danger' ? colors.dangerTint : colors.warningTint }]} accessibilityRole="alert" accessibilityLiveRegion="assertive">
      <Icon name="alert-circle" size={18} color={fg} />
      <Text variant="small" color={fg} style={{ flex: 1 }}>
        {children}
      </Text>
    </View>
  );
}

export function PublishHomework({ today, onShowMine }: { today: string; onShowMine: () => void }) {
  const client = useQueryClient();
  const timetable = useMyTimetable();
  const options = teachingOptions(timetable.data);

  const [sectionId, setSectionId] = useState<number | null>(null);
  const [subjectId, setSubjectId] = useState<number | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [due, setDue] = useState<string | null>(null);
  const [file, setFile] = useState<PickedFile | null>(null);
  const [errors, setErrors] = useState<Errors>({});
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [stage, setStage] = useState<Stage>({ kind: 'form' });

  const section = options.find((o) => o.sectionId === sectionId);
  const refreshList = () => void client.invalidateQueries({ queryKey: teacherKeys.homework });

  function reset() {
    setSectionId(null);
    setSubjectId(null);
    setTitle('');
    setDescription('');
    setDue(null);
    setFile(null);
    setErrors({});
    setFormError(null);
    setStage({ kind: 'form' });
  }

  function validate(): Errors {
    const e: Errors = {};
    if (sectionId === null) e.section = 'Choose a class and section.';
    if (subjectId === null) e.subject = 'Choose a subject.';
    if (!title.trim()) e.title = 'Enter a title.';
    else if (title.trim().length > HOMEWORK_TITLE_MAX) e.title = `At most ${HOMEWORK_TITLE_MAX} characters.`;
    if (!due) e.due = 'Choose a due date.';
    else if (diffDays(due, today) < 0) e.due = 'The due date cannot be in the past.';
    return e;
  }

  async function publish() {
    if (busy) return;
    const e = validate();
    setErrors(e);
    setFormError(null);
    if (Object.keys(e).length > 0 || sectionId === null || subjectId === null || !due) return;

    setBusy(true);
    let created: MyHomework;
    try {
      created = await publishHomework({ sectionId, subjectId, title: title.trim(), description: description.trim() || undefined, dueDate: due });
    } catch (err) {
      setFormError(messageOf(err)); // nothing was published
      setBusy(false);
      return;
    }
    refreshList();
    if (!file) {
      setStage({ kind: 'done', title: created.title, note: null });
      setBusy(false);
      return;
    }
    try {
      await uploadHomeworkAttachment(created.homeworkId, file);
      refreshList();
      setStage({ kind: 'done', title: created.title, note: null });
    } catch (err) {
      setStage({ kind: 'attachmentFailed', created, file, message: messageOf(err) });
    } finally {
      setBusy(false);
    }
  }

  async function retryUpload(s: Extract<Stage, { kind: 'attachmentFailed' }>) {
    if (busy) return;
    setBusy(true);
    try {
      await uploadHomeworkAttachment(s.created.homeworkId, s.file);
      refreshList();
      setStage({ kind: 'done', title: s.created.title, note: null });
    } catch (err) {
      setStage({ ...s, message: messageOf(err) });
    } finally {
      setBusy(false);
    }
  }

  if (stage.kind === 'done') {
    return (
      <Card style={{ gap: 12, alignItems: 'center' }}>
        <Icon name="check-circle" size={40} color={colors.success} />
        <Text variant="heading">Homework published</Text>
        <Text variant="body" style={{ textAlign: 'center' }}>
          {stage.title}
        </Text>
        {stage.note ? <Text variant="small">{stage.note}</Text> : null}
        <Button title="Publish another" icon="plus" onPress={reset} style={{ alignSelf: 'stretch' }} />
        <Button title="See my homework" variant="secondary" onPress={onShowMine} style={{ alignSelf: 'stretch' }} />
      </Card>
    );
  }

  if (stage.kind === 'attachmentFailed') {
    return (
      <Card style={{ gap: 12 }}>
        <Banner tone="warning">{`"${stage.created.title}" was published, but its attachment could not be uploaded: ${stage.message}`}</Banner>
        <Text variant="small">Students can already see the homework. You can retry the attachment now, or continue without it.</Text>
        <Button title="Retry upload" icon="upload" onPress={() => retryUpload(stage)} loading={busy} />
        <Button
          title="Continue without attachment"
          variant="secondary"
          disabled={busy}
          onPress={() => setStage({ kind: 'done', title: stage.created.title, note: 'Published without an attachment.' })}
        />
      </Card>
    );
  }

  return (
    <QueryState loading={timetable.isPending} error={timetable.error} onRetry={() => void timetable.refetch()} skeletons={2}>
      <Card style={{ gap: 16 }}>
        {options.length === 0 ? <Banner tone="warning">You have no classes in the timetable yet, so there is nothing to set homework for. Ask the school office.</Banner> : null}
        <Select
          label="Class and section"
          value={sectionId}
          options={options.map((o) => ({ value: o.sectionId, label: o.label }))}
          placeholder="Choose class and section"
          error={errors.section}
          onChange={(v) => {
            setSectionId(v);
            setSubjectId(null); // the subjects depend on the section
          }}
        />
        <Select
          label="Subject"
          value={subjectId}
          options={(section?.subjects ?? []).map((s) => ({ value: s.subjectId, label: s.subjectName }))}
          placeholder={section ? 'Choose subject' : 'Choose a section first'}
          disabled={!section}
          error={errors.subject}
          onChange={setSubjectId}
        />
        <Input label="Title" value={title} onChangeText={setTitle} maxLength={HOMEWORK_TITLE_MAX} error={errors.title} editable={!busy} />
        <Input
          label="Details (optional)"
          value={description}
          onChangeText={setDescription}
          multiline
          maxLength={HOMEWORK_DESCRIPTION_MAX}
          showCount
          editable={!busy}
        />
        <DateField label="Due date" value={due} onChange={setDue} min={today} initial={today} error={errors.due} />
        <FileField label="Attachment" value={file} onChange={setFile} disabled={busy} />
        <Text variant="small">The class, section and subject cannot be changed after publishing.</Text>
        {formError ? <Banner tone="danger">{formError}</Banner> : null}
        <Button title="Publish" icon="send" onPress={publish} loading={busy} disabled={options.length === 0} />
      </Card>
    </QueryState>
  );
}

const styles = StyleSheet.create({
  banner: { flexDirection: 'row', alignItems: 'flex-start', gap: 8, padding: 12, borderRadius: radii.input },
});
