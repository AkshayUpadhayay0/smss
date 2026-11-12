import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { Icon } from '../../components/Icon';
import { HomeHeader } from '../../components/HomeHeader';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { apiDayOfWeek, formatTime } from '../../lib/dates';
import { classLabel } from '../../lib/format';
import { colors, MIN_TOUCH, radii } from '../../theme/tokens';
import { SectionHeader } from '../student/parts';
import { distinctSectionCount, periodsForDay, splitLeave } from './derive';
import { useMySections, useMyTimetable, useStudentLeave, useTeacherDashboard } from './queries';

// Names the server uses in TeacherDashboard.partsUnavailable (see TeacherDashboardParts in smss-api).
const PART_STUDENTS = 'totalStudents';
const PART_FEES = 'feePending';
const PART_CLASS_TEACHER = 'classTeacherOf';

function Tile({ icon, label, value }: { icon: 'users' | 'layers' | 'credit-card'; label: string; value: number }) {
  return (
    <View style={styles.tile} accessible accessibilityLabel={`${label}: ${value}`}>
      <Icon name={icon} size={18} color={colors.primaryText} />
      <Text variant="title" color={colors.ink}>
        {value}
      </Text>
      <Text variant="small">{label}</Text>
    </View>
  );
}

export function TeacherHome() {
  const router = useRouter();
  const go = (path: string) => router.navigate(path as Href);
  const dashboard = useTeacherDashboard();
  const timetable = useMyTimetable();
  const d = dashboard.data;
  const isClassTeacher = !!d && d.classTeacherOf.length > 0;
  const mySections = useMySections(isClassTeacher);
  const leave = useStudentLeave(isClassTeacher);

  const refresh = () => {
    void dashboard.refetch();
    void timetable.refetch();
    if (isClassTeacher) {
      void mySections.refetch();
      void leave.refetch();
    }
  };

  const unavailable = new Set(d?.partsUnavailable ?? []);
  // "My periods today" comes from the teacher's own weekly timetable, filtered by the weekday of the SERVER's date.
  const todayDow = d ? apiDayOfWeek(d.today) : null;
  const periods = d && timetable.data && todayDow !== null && todayDow <= 6 ? periodsForDay(timetable.data, todayDow) : [];
  const sectionCount = timetable.data ? distinctSectionCount(timetable.data) : null;
  const pendingLeave = leave.data ? splitLeave(leave.data).pending.length : null;

  return (
    <Screen onRefresh={refresh} refreshing={dashboard.isRefetching || timetable.isRefetching}>
      <HomeHeader subtitle={d?.profile.designationName ?? 'Teacher'} />

      <QueryState loading={dashboard.isPending} error={dashboard.error} onRetry={refresh} skeletons={2}>
        {d ? (
          <>
            {/* Class teacher card: only when this teacher IS a class teacher */}
            {unavailable.has(PART_CLASS_TEACHER) ? (
              <Card>
                <Text variant="small">Your class-teacher details are unavailable right now. Pull down to try again.</Text>
              </Card>
            ) : (
              d.classTeacherOf.map((section) => {
                const ms = mySections.data?.sections.find((s) => s.sectionId === section.sectionId);
                // Prefer the per-section answer; fall back to the dashboard's own flag when there is a single section.
                const markedToday = ms ? ms.markedToday : d.classTeacherOf.length === 1 ? !d.notMarkedYet : undefined;
                const label = classLabel(section.className, section.sectionName) ?? 'Your class';
                return (
                  <Card key={section.sectionId} style={{ gap: 10 }}>
                    <View style={styles.rowBetween}>
                      <View style={{ flex: 1 }}>
                        <Text variant="label" color={colors.muted}>
                          Class teacher
                        </Text>
                        <Text variant="title">{label}</Text>
                        {ms ? <Text variant="small">{ms.rosterCount} students</Text> : null}
                      </View>
                      {markedToday === undefined ? null : markedToday ? (
                        <Chip label="Marked today" tone="success" icon="check" />
                      ) : (
                        <Chip label="Not marked today" tone="warning" icon="alert-circle" />
                      )}
                    </View>
                    {markedToday && d.classTeacherOf.length === 1 && d.presentToday !== null && d.absentToday !== null ? (
                      <Text variant="small">
                        Today: {d.presentToday} present · {d.absentToday} absent
                      </Text>
                    ) : null}
                    <Button
                      title={markedToday ? "Update today's attendance" : "Mark today's attendance"}
                      icon="check-square"
                      onPress={() => go(`/(teacher)/mark-attendance/${section.sectionId}`)}
                    />
                  </Card>
                );
              })
            )}

            {/* Tiles: a tile whose part the server could not load (or that does not apply) is hidden, never shown as 0 */}
            {(() => {
              const tiles: React.ReactNode[] = [];
              if (!unavailable.has(PART_STUDENTS) && d.totalStudents !== null) tiles.push(<Tile key="students" icon="users" label="Students in your class" value={d.totalStudents} />);
              if (sectionCount !== null) tiles.push(<Tile key="sections" icon="layers" label="Sections you teach" value={sectionCount} />);
              if (!unavailable.has(PART_FEES) && d.feePending !== null) tiles.push(<Tile key="fees" icon="credit-card" label="Students with fees pending" value={d.feePending} />);
              return tiles.length ? <View style={styles.tiles}>{tiles}</View> : null;
            })()}

            {/* My periods today */}
            <Card style={{ gap: 8 }}>
              <SectionHeader title="My periods today" />
              {timetable.isPending ? (
                <Text variant="small">Loading…</Text>
              ) : timetable.isError ? (
                <Button title="Retry" icon="refresh-cw" variant="secondary" onPress={() => void timetable.refetch()} />
              ) : periods.length === 0 ? (
                <Text variant="small">No classes today.</Text>
              ) : (
                periods.map((p) => (
                  <View key={p.periodNumber} style={styles.periodRow} accessible accessibilityLabel={`Period ${p.periodNumber}, ${p.subjectName}, ${p.sectionLabel}, ${formatTime(p.startTime)} to ${formatTime(p.endTime)}`}>
                    <View style={styles.num}>
                      <Text variant="label" weight="bold" color={colors.primaryText}>
                        {p.periodNumber}
                      </Text>
                    </View>
                    <View style={{ flex: 1 }}>
                      <Text variant="heading" style={{ fontSize: 15 }}>
                        {p.subjectName}
                      </Text>
                      <Text variant="small">
                        {p.sectionLabel}
                        {p.roomNo ? ` · Room ${p.roomNo}` : ''}
                      </Text>
                    </View>
                    <Text variant="small" style={{ textAlign: 'right' }}>
                      {formatTime(p.startTime)}
                      {'\n'}
                      {formatTime(p.endTime)}
                    </Text>
                  </View>
                ))
              )}
            </Card>

            {/* Pending student leave (class teachers only) */}
            {isClassTeacher ? (
              <Pressable onPress={() => go('/(teacher)/(tabs)/leave')} accessibilityRole="button" accessibilityLabel={pendingLeave === null ? 'Student leave' : `Student leave, ${pendingLeave} pending`}>
                <Card style={styles.leaveRow}>
                  <View style={{ flex: 1 }}>
                    <Text variant="heading" style={{ fontSize: 15 }}>
                      Student leave
                    </Text>
                    <Text variant="small">
                      {leave.isPending ? 'Checking…' : leave.isError ? 'Could not load. Open to retry.' : pendingLeave === 0 ? 'No requests waiting' : `${pendingLeave} request${pendingLeave === 1 ? '' : 's'} waiting for you`}
                    </Text>
                  </View>
                  {pendingLeave ? <Chip label={`${pendingLeave} pending`} tone="warning" icon="clock" /> : null}
                  <Icon name="chevron-right" size={20} color={colors.muted} />
                </Card>
              </Pressable>
            ) : null}
          </>
        ) : null}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  rowBetween: { flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: 8 },
  tiles: { flexDirection: 'row', flexWrap: 'wrap', gap: 10 },
  tile: { flexGrow: 1, flexBasis: '30%', minWidth: 100, gap: 4, padding: 12, borderRadius: radii.card, backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border },
  periodRow: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: MIN_TOUCH + 8 },
  num: { width: 36, height: 36, borderRadius: 10, backgroundColor: colors.tint, alignItems: 'center', justifyContent: 'center' },
  leaveRow: { flexDirection: 'row', alignItems: 'center', gap: 12 },
});
