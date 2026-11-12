import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Pressable, StyleSheet, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { messageOf } from '../../api/errors';
import { createAssessment, type RosterStudent } from '../../api/teacher';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Icon } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { formatFull } from '../../lib/dates';
import { formatMarks } from '../../lib/format';
import { colors, fonts, MIN_TOUCH, radii } from '../../theme/tokens';
import type { TestSetup } from './MarksSetup';
import { teacherKeys, useAssessmentRoster } from './queries';
import { checkMark } from './rules';

interface Row {
  text: string;
  absent: boolean;
}

function Summary({ setup, onEdit, disabled }: { setup: TestSetup; onEdit: () => void; disabled: boolean }) {
  return (
    <Card style={{ gap: 6 }}>
      <View style={styles.between}>
        <Text variant="heading" style={{ flex: 1 }}>
          {setup.title}
        </Text>
        <Button title="Edit" icon="edit-2" variant="secondary" onPress={onEdit} disabled={disabled} />
      </View>
      <Text variant="small">
        {setup.sectionLabel} · {setup.subjectName}
      </Text>
      <Text variant="small">
        {setup.typeName} · {formatFull(setup.testDate)} · Max {formatMarks(setup.maxMarks)}
      </Text>
    </Card>
  );
}

function Roster({ setup, roster, onEdit, onRecordAnother, onRefresh, refreshing }: { setup: TestSetup; roster: RosterStudent[]; onEdit: () => void; onRecordAnother: () => void; onRefresh: () => void; refreshing: boolean }) {
  const client = useQueryClient();
  const [rows, setRows] = useState<Record<string, Row>>({});
  const [recorded, setRecorded] = useState(false);

  const checks = roster.map((s) => {
    const row = rows[s.studentId] ?? { text: '', absent: false };
    const check = row.absent ? ({ state: 'ok', value: 0 } as const) : checkMark(row.text, setup.maxMarks);
    return { student: s, row, check };
  });
  const covered = checks.filter((c) => c.check.state === 'ok').length;
  const allCovered = roster.length > 0 && covered === roster.length;

  const submit = useMutation({
    mutationFn: () =>
      createAssessment({
        academicYearId: setup.academicYearId,
        sectionId: setup.sectionId,
        subjectId: setup.subjectId,
        assessmentTypeId: setup.typeId,
        title: setup.title,
        testDate: setup.testDate,
        maxMarks: setup.maxMarks,
        marks: checks.map(({ student, row, check }) =>
          row.absent ? { studentId: student.studentId, isAbsent: true } : { studentId: student.studentId, obtainedMarks: check.state === 'ok' ? check.value : undefined },
        ),
      }),
    onSuccess: () => {
      setRecorded(true);
      void client.invalidateQueries({ queryKey: teacherKeys.dashboard });
    },
  });

  function setRow(id: string, patch: Partial<Row>) {
    setRows((prev) => ({ ...prev, [id]: { ...(prev[id] ?? { text: '', absent: false }), ...patch } }));
  }

  if (recorded) {
    return (
      <Screen title="Enter marks">
        <Card style={{ gap: 12, alignItems: 'center' }}>
          <Icon name="check-circle" size={40} color={colors.success} />
          <Text variant="heading">Test recorded</Text>
          <Text variant="body" style={{ textAlign: 'center' }}>
            {setup.title} · {setup.sectionLabel} · {setup.subjectName}
          </Text>
          <Button title="Record another test" icon="plus" onPress={onRecordAnother} style={{ alignSelf: 'stretch' }} />
        </Card>
      </Screen>
    );
  }

  const footer = (
    <SafeAreaView edges={['left', 'right']} style={styles.footer}>
      <Text variant="label" color={allCovered ? colors.success : colors.warningText} accessibilityLiveRegion="polite">
        {covered} of {roster.length} covered
      </Text>
      <Button title="Submit marks" icon="send" onPress={() => submit.mutate()} disabled={!allCovered} loading={submit.isPending} />
    </SafeAreaView>
  );

  return (
    <Screen title="Enter marks" onBack={onEdit} footer={footer} onRefresh={onRefresh} refreshing={refreshing}>
      <Summary setup={setup} onEdit={onEdit} disabled={submit.isPending} />
      {submit.isError ? (
        <View style={styles.error} accessibilityRole="alert" accessibilityLiveRegion="assertive">
          <Icon name="alert-circle" size={18} color={colors.danger} />
          <Text variant="small" color={colors.danger} style={{ flex: 1 }}>
            {messageOf(submit.error)}
          </Text>
        </View>
      ) : null}

      {checks.map(({ student, row, check }) => {
        const incomplete = check.state === 'empty';
        const invalid = check.state === 'invalid';
        return (
          <Card key={student.studentId} style={[styles.row, incomplete && styles.incomplete, invalid && styles.invalid]}>
            <View style={{ flex: 1 }}>
              <Text variant="heading" style={{ fontSize: 15 }} numberOfLines={1}>
                {student.fullName}
              </Text>
              <Text variant="small">{student.rollNumber ? `Roll ${student.rollNumber}` : ' '}</Text>
              {incomplete ? (
                <Text variant="small" color={colors.warningText} weight="semibold">
                  Enter marks or Absent
                </Text>
              ) : null}
              {invalid ? (
                <Text variant="small" color={colors.danger} weight="semibold" accessibilityLiveRegion="polite">
                  {check.message}
                </Text>
              ) : null}
            </View>
            <TextInput
              value={row.absent ? '' : row.text}
              onChangeText={(t) => setRow(student.studentId, { text: t })}
              editable={!row.absent && !submit.isPending}
              keyboardType="decimal-pad"
              placeholder={row.absent ? '—' : `/ ${formatMarks(setup.maxMarks)}`}
              placeholderTextColor={colors.muted}
              accessibilityLabel={`Marks for ${student.fullName}, out of ${formatMarks(setup.maxMarks)}`}
              style={[styles.input, row.absent && { backgroundColor: colors.divider }, invalid && { borderColor: colors.danger }]}
            />
            <Pressable
              onPress={() => setRow(student.studentId, { absent: !row.absent, text: '' })}
              disabled={submit.isPending}
              accessibilityRole="checkbox"
              accessibilityState={{ checked: row.absent }}
              accessibilityLabel={`${student.fullName} absent`}
              style={[styles.absent, row.absent && { backgroundColor: colors.danger, borderColor: colors.danger }]}
            >
              <Icon name={row.absent ? 'user-x' : 'user'} size={16} color={row.absent ? colors.onPrimary : colors.muted} />
              <Text variant="small" weight="semibold" color={row.absent ? colors.onPrimary : colors.inkSoft}>
                Absent
              </Text>
            </Pressable>
          </Card>
        );
      })}

    </Screen>
  );
}

export function MarksEntry({ setup, onEdit, onRecordAnother }: { setup: TestSetup; onEdit: () => void; onRecordAnother: () => void }) {
  const roster = useAssessmentRoster(setup.academicYearId, setup.sectionId, setup.subjectId);
  const reload = () => void roster.refetch();
  if (roster.data && roster.data.length > 0) {
    // Pull-to-refresh reloads the roster and resets the typed marks (an explicit gesture); nothing refetches silently.
    return <Roster key={roster.dataUpdatedAt} setup={setup} roster={roster.data} onEdit={onEdit} onRecordAnother={onRecordAnother} onRefresh={reload} refreshing={roster.isRefetching} />;
  }
  return (
    <Screen title="Enter marks" onBack={onEdit} onRefresh={reload} refreshing={roster.isRefetching}>
      <QueryState
        loading={roster.isPending}
        error={roster.error}
        onRetry={reload}
        empty={roster.data ? { title: 'No students', message: 'This section has no active students, so there is nothing to record.', icon: 'users' } : null}
      >
        {null}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  between: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 8, paddingVertical: 10 },
  incomplete: { backgroundColor: colors.warningTint, borderColor: colors.warningTint },
  invalid: { borderColor: colors.danger },
  input: { width: 80, minHeight: MIN_TOUCH, borderRadius: radii.input, borderWidth: 1.5, borderColor: colors.inputBorder, backgroundColor: colors.surface, textAlign: 'center', fontFamily: fonts.semibold, fontSize: 16, color: colors.ink },
  absent: { alignItems: 'center', justifyContent: 'center', minHeight: MIN_TOUCH, minWidth: 60, paddingHorizontal: 6, borderRadius: radii.chip, borderWidth: 1.5, borderColor: colors.border, backgroundColor: colors.surface },
  error: { flexDirection: 'row', alignItems: 'flex-start', gap: 8, padding: 12, borderRadius: radii.input, backgroundColor: colors.dangerTint },
  footer: { gap: 8, padding: 16, backgroundColor: colors.surface, borderTopWidth: 1, borderTopColor: colors.border },
});
