import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { messageOf } from '../../api/errors';
import { submitAttendance, type SectionAttendance } from '../../api/teacher';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Icon } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { formatFull } from '../../lib/dates';
import { classLabel } from '../../lib/format';
import { colors, MIN_TOUCH, radii } from '../../theme/tokens';
import { teacherKeys, useSectionAttendance } from './queries';
import { tallyAttendance } from './rules';

type Mark = 'PRESENT' | 'ABSENT' | undefined;

function seed(data: SectionAttendance): Record<string, Mark> {
  const out: Record<string, Mark> = {};
  for (const s of data.students) {
    const code = s.statusCode?.toUpperCase();
    if (s.onRoster && (code === 'PRESENT' || code === 'ABSENT')) out[s.studentId] = code;
  }
  return out;
}

function MarkButtons({ value, onChange, disabled, name }: { value: Mark; onChange: (m: 'PRESENT' | 'ABSENT') => void; disabled: boolean; name: string }) {
  return (
    <View style={styles.seg}>
      <Pressable
        onPress={() => onChange('PRESENT')}
        disabled={disabled}
        accessibilityRole="button"
        accessibilityLabel={`${name}: mark present`}
        accessibilityState={{ selected: value === 'PRESENT', disabled }}
        style={[styles.segBtn, value === 'PRESENT' && { backgroundColor: colors.successTint, borderColor: colors.success }]}
      >
        <Icon name="check" size={16} color={value === 'PRESENT' ? colors.success : colors.muted} />
        <Text variant="label" color={value === 'PRESENT' ? colors.success : colors.inkSoft} weight={value === 'PRESENT' ? 'bold' : 'semibold'}>
          Present
        </Text>
      </Pressable>
      <Pressable
        onPress={() => onChange('ABSENT')}
        disabled={disabled}
        accessibilityRole="button"
        accessibilityLabel={`${name}: mark absent`}
        accessibilityState={{ selected: value === 'ABSENT', disabled }}
        style={[styles.segBtn, value === 'ABSENT' && { backgroundColor: colors.danger, borderColor: colors.danger }]}
      >
        <Icon name="x" size={16} color={value === 'ABSENT' ? colors.onPrimary : colors.muted} />
        <Text variant="label" color={value === 'ABSENT' ? colors.onPrimary : colors.inkSoft} weight={value === 'ABSENT' ? 'bold' : 'semibold'}>
          Absent
        </Text>
      </Pressable>
    </View>
  );
}

/** Why Submit is unavailable, from the server's own canSubmit flag plus what it tells us. */
function cannotSubmitReason(d: SectionAttendance): string {
  if (d.rosterCount === 0) return 'This section has no active students, so there is nothing to mark.';
  if (!d.isToday) return "Attendance can only be marked for today. Past days are corrected by the school office.";
  return 'You can only mark attendance for a section you are the class teacher of.';
}

function Form({ data, sectionId, onRefresh, refreshing }: { data: SectionAttendance; sectionId: number; onRefresh: () => void; refreshing: boolean }) {
  const router = useRouter();
  const client = useQueryClient();
  const roster = data.students.filter((s) => s.onRoster);
  const ids = roster.map((s) => s.studentId);
  const [marks, setMarks] = useState<Record<string, Mark>>(() => seed(data));
  const [done, setDone] = useState<{ present: number; absent: number } | null>(null);

  const presentId = data.statuses.find((s) => s.statusCode.toUpperCase() === 'PRESENT')?.attendanceStatusId;
  const absentId = data.statuses.find((s) => s.statusCode.toUpperCase() === 'ABSENT')?.attendanceStatusId;
  const tally = tallyAttendance(ids, marks);
  const complete = ids.length > 0 && tally.unmarked === 0;
  const canSubmit = data.canSubmit && presentId !== undefined && absentId !== undefined && complete;

  const submit = useMutation({
    mutationFn: () =>
      submitAttendance({
        sectionId,
        date: data.date, // the SERVER's date for this roster
        records: ids.map((studentId) => ({ studentId, attendanceStatusId: (marks[studentId] === 'PRESENT' ? presentId : absentId) as number })),
      }),
    onSuccess: (res) => {
      setDone({ present: res.present, absent: res.absent });
      void client.invalidateQueries({ queryKey: teacherKeys.mySections });
      void client.invalidateQueries({ queryKey: teacherKeys.dashboard });
    },
  });

  if (done) {
    return (
      <Screen title="Attendance submitted" onBack={() => router.back()} edges={['top', 'left', 'right', 'bottom']}>
        <Card style={{ gap: 12, alignItems: 'center' }}>
          <Icon name="check-circle" size={40} color={colors.success} />
          <Text variant="heading">Attendance saved</Text>
          <Text variant="body">
            {done.present} present · {done.absent} absent
          </Text>
          <Button title="Done" onPress={() => router.back()} style={{ alignSelf: 'stretch' }} />
        </Card>
      </Screen>
    );
  }

  const footer = (
    <SafeAreaView edges={['bottom', 'left', 'right']} style={styles.footer}>
      <View style={styles.counts} accessibilityLiveRegion="polite" accessible accessibilityLabel={`${tally.present} present, ${tally.absent} absent, ${tally.unmarked} not marked`}>
        <Text variant="label" color={colors.success}>
          Present {tally.present}
        </Text>
        <Text variant="label" color={colors.danger}>
          Absent {tally.absent}
        </Text>
        <Text variant="label" color={tally.unmarked ? colors.warningText : colors.muted}>
          Not marked {tally.unmarked}
        </Text>
      </View>
      <Button title="Submit attendance" icon="send" onPress={() => submit.mutate()} disabled={!canSubmit} loading={submit.isPending} />
    </SafeAreaView>
  );

  return (
    <Screen
      title="Mark attendance"
      subtitle={`${classLabel(data.className, data.sectionName)} · ${formatFull(data.date)}`}
      onBack={() => router.back()}
      edges={['top', 'left', 'right']}
      footer={footer}
      onRefresh={onRefresh}
      refreshing={refreshing}
    >
      {!data.canSubmit ? (
        <View style={styles.banner} accessibilityRole="alert">
          <Icon name="info" size={18} color={colors.warningText} />
          <Text variant="small" color={colors.warningText} style={{ flex: 1 }}>
            {cannotSubmitReason(data)}
          </Text>
        </View>
      ) : null}
      {data.isMarked && data.canSubmit ? (
        <Text variant="small">Attendance was already marked today. Submitting again replaces it.</Text>
      ) : null}
      {submit.isError ? (
        <View style={[styles.banner, { backgroundColor: colors.dangerTint }]} accessibilityRole="alert" accessibilityLiveRegion="assertive">
          <Icon name="alert-circle" size={18} color={colors.danger} />
          <Text variant="small" color={colors.danger} style={{ flex: 1 }}>
            {messageOf(submit.error)}
          </Text>
        </View>
      ) : null}

      <Button
        title="Mark all present"
        icon="check-circle"
        variant="secondary"
        disabled={!data.canSubmit || roster.length === 0}
        onPress={() => setMarks(Object.fromEntries(ids.map((id) => [id, 'PRESENT' as const])))}
      />

      {roster.map((s) => (
        <Card key={s.studentId} style={styles.row}>
          <View style={{ flex: 1 }}>
            <Text variant="heading" style={{ fontSize: 15 }} numberOfLines={1}>
              {s.fullName}
            </Text>
            <Text variant="small">{s.rollNumber ? `Roll ${s.rollNumber}` : ' '}</Text>
          </View>
          <MarkButtons
            name={s.fullName}
            value={marks[s.studentId]}
            disabled={!data.canSubmit || submit.isPending}
            onChange={(m) => setMarks((prev) => ({ ...prev, [s.studentId]: m }))}
          />
        </Card>
      ))}
    </Screen>
  );
}

export function MarkAttendanceScreen({ sectionId }: { sectionId: number }) {
  const router = useRouter();
  const q = useSectionAttendance(sectionId);
  // Pull-to-refresh reloads the roster and reseeds the form (an explicit gesture); nothing refetches silently under unsaved marks.
  if (q.data) return <Form key={q.dataUpdatedAt} data={q.data} sectionId={sectionId} onRefresh={() => void q.refetch()} refreshing={q.isRefetching} />;
  return (
    <Screen title="Mark attendance" onBack={() => router.back()} edges={['top', 'left', 'right', 'bottom']} onRefresh={() => void q.refetch()} refreshing={q.isRefetching}>
      <QueryState loading={q.isPending} error={q.error} onRetry={() => void q.refetch()}>
        {null}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 10, paddingVertical: 12 },
  seg: { flexDirection: 'row', gap: 6 },
  segBtn: { flexDirection: 'row', alignItems: 'center', gap: 4, minHeight: MIN_TOUCH, minWidth: 84, paddingHorizontal: 10, justifyContent: 'center', borderRadius: radii.chip, borderWidth: 1.5, borderColor: colors.border, backgroundColor: colors.surface },
  banner: { flexDirection: 'row', alignItems: 'flex-start', gap: 8, padding: 12, borderRadius: radii.input, backgroundColor: colors.warningTint },
  footer: { backgroundColor: colors.surface, borderTopWidth: 1, borderTopColor: colors.border, paddingHorizontal: 16, paddingTop: 10, paddingBottom: 10, gap: 10 },
  counts: { flexDirection: 'row', justifyContent: 'space-between' },
});
