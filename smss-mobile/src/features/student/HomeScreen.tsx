import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { ErrorState } from '../../components/ErrorState';
import { HomeHeader } from '../../components/HomeHeader';
import { Icon } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Skeleton } from '../../components/Skeleton';
import { Text } from '../../components/Text';
import { messageOf } from '../../api/errors';
import { classLabel } from '../../lib/format';
import { diffDays, formatShort, formatTime } from '../../lib/dates';
import { colors, MIN_TOUCH, radii } from '../../theme/tokens';
import { useDashboard, useHomework, useUnreadCount } from './queries';
import { DueChip, LeaveStatusChip, SectionHeader } from './parts';

function Bell({ count, onPress }: { count: number; onPress: () => void }) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={count > 0 ? `Notices, ${count} unread` : 'Notices'}
      style={styles.bell}
    >
      <Icon name="bell" size={22} color={colors.ink} />
      {count > 0 ? (
        <View style={styles.badge}>
          <Text variant="tab" color={colors.onPrimary} weight="bold" style={{ fontSize: 10 }}>
            {count > 9 ? '9+' : String(count)}
          </Text>
        </View>
      ) : null}
    </Pressable>
  );
}

export function StudentHome() {
  const router = useRouter();
  const go = (path: string) => router.navigate(path as Href);
  const dashboard = useDashboard();
  const homework = useHomework();
  const unread = useUnreadCount();

  const d = dashboard.data;
  const today = d?.today;
  const unreadCount = unread.data ?? d?.unreadNoticesCount ?? 0;
  const refreshing = dashboard.isRefetching || homework.isRefetching;
  const refresh = () => {
    void dashboard.refetch();
    void homework.refetch();
    void unread.refetch();
  };

  const classText = d ? classLabel(d.profile.className, d.profile.sectionName) : null;
  const dueSoon =
    today && homework.data
      ? homework.data
          .filter((h) => diffDays(h.dueDate, today) >= 0)
          .sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.homeworkId - b.homeworkId)
          .slice(0, 2)
      : [];
  const latestLeave = d?.recentLeave.length ? [...d.recentLeave].sort((a, b) => b.appliedAt.localeCompare(a.appliedAt))[0] : null;

  return (
    <Screen onRefresh={refresh} refreshing={refreshing}>
      <HomeHeader subtitle={classText} classText={classText} trailing={<Bell count={unreadCount} onPress={() => go('/(student)/(tabs)/notices')} />} />

      <QueryState loading={dashboard.isPending} error={dashboard.error} onRetry={refresh} skeletons={2}>
        {d && today ? (
          <>
            {/* Attendance summary */}
            {d.attendance ? (
              <Pressable onPress={() => go('/(student)/attendance')} accessibilityRole="button" accessibilityLabel="Attendance details">
                <Card style={{ gap: 12 }}>
                  <View style={styles.rowBetween}>
                    <Text variant="heading">Attendance</Text>
                    <Icon name="chevron-right" size={20} color={colors.muted} />
                  </View>
                  <Text variant="label" color={colors.muted}>
                    Academic year to date
                  </Text>
                  <Text variant="display" color={colors.primaryText}>
                    {d.attendance.presentPercentage === null ? '—' : `${d.attendance.presentPercentage}%`}
                  </Text>
                  <View
                    style={styles.track}
                    accessible
                    accessibilityRole="progressbar"
                    accessibilityLabel={`Attendance for the academic year to date, ${d.attendance.presentPercentage ?? 0} percent`}
                  >
                    <View style={[styles.fill, { width: `${Math.max(0, Math.min(100, d.attendance.presentPercentage ?? 0))}%` }]} />
                  </View>
                  <View style={styles.stats}>
                    <Text variant="label" color={colors.success}>
                      Present {d.attendance.presentCount}
                    </Text>
                    <Text variant="label" color={colors.danger}>
                      Absent {d.attendance.absentCount}
                    </Text>
                  </View>
                </Card>
              </Pressable>
            ) : (
              <Card>
                <Text variant="heading">Attendance</Text>
                <Text variant="small">Attendance is unavailable right now. Pull down to try again.</Text>
              </Card>
            )}

            {/* Today's classes */}
            <Card style={{ gap: 8 }}>
              <SectionHeader title="Today's classes" actionLabel="Timetable" onAction={() => go('/(student)/(tabs)/timetable')} />
              {d.todayClasses.length === 0 ? (
                <Text variant="small">No classes today.</Text>
              ) : (
                d.todayClasses.slice(0, 3).map((c) => (
                  <View key={c.periodNumber} style={styles.classRow}>
                    <View style={styles.badgeNum}>
                      <Text variant="label" color={colors.primaryText} weight="bold">
                        {c.periodNumber}
                      </Text>
                    </View>
                    <View style={{ flex: 1 }}>
                      <Text variant="heading" style={{ fontSize: 15 }}>
                        {c.subjectName}
                      </Text>
                      <Text variant="small">
                        {c.teacherName} · {formatTime(c.startTime)} – {formatTime(c.endTime)}
                      </Text>
                    </View>
                    {c.status === 'InProgress' ? <Chip label="Now" tone="primary" icon="play-circle" /> : null}
                    {c.status === 'Completed' ? <Chip label="Done" tone="neutral" icon="check" /> : null}
                  </View>
                ))
              )}
            </Card>

            {/* Homework due */}
            <Card style={{ gap: 8 }}>
              <SectionHeader title="Homework due" actionLabel="See all" onAction={() => go('/(student)/(tabs)/homework')} />
              {homework.isPending ? (
                <Skeleton height={40} />
              ) : homework.isError ? (
                <ErrorState message={messageOf(homework.error)} onRetry={() => void homework.refetch()} />
              ) : dueSoon.length === 0 ? (
                <Text variant="small">Nothing due. Enjoy the break!</Text>
              ) : (
                dueSoon.map((h) => (
                  <View key={h.homeworkId} style={styles.hwRow}>
                    <View style={{ flex: 1 }}>
                      <Text variant="heading" style={{ fontSize: 15 }} numberOfLines={1}>
                        {h.title}
                      </Text>
                      <Text variant="small">{h.subjectName}</Text>
                    </View>
                    <DueChip dueDate={h.dueDate} today={today} />
                  </View>
                ))
              )}
            </Card>

            {/* Leave request status */}
            {latestLeave ? (
              <Card style={styles.leaveRow}>
                <View style={{ flex: 1 }}>
                  <Text variant="heading" style={{ fontSize: 15 }}>
                    Leave request
                  </Text>
                  <Text variant="small">
                    {latestLeave.leaveTypeName} · {formatShort(latestLeave.fromDate)}
                    {latestLeave.toDate !== latestLeave.fromDate ? ` – ${formatShort(latestLeave.toDate)}` : ''}
                  </Text>
                </View>
                <LeaveStatusChip status={latestLeave.status} />
              </Card>
            ) : null}
          </>
        ) : (
          <Button title="Reload" variant="secondary" icon="refresh-cw" onPress={refresh} />
        )}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  bell: { width: MIN_TOUCH, height: MIN_TOUCH, borderRadius: MIN_TOUCH / 2, backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border, alignItems: 'center', justifyContent: 'center' },
  badge: { position: 'absolute', top: -2, right: -2, minWidth: 18, height: 18, borderRadius: 9, paddingHorizontal: 4, backgroundColor: colors.danger, alignItems: 'center', justifyContent: 'center' },
  rowBetween: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  track: { height: 10, borderRadius: 5, backgroundColor: colors.tint, overflow: 'hidden' },
  fill: { height: 10, borderRadius: 5, backgroundColor: colors.primary },
  stats: { flexDirection: 'row', alignItems: 'center', gap: 16, flexWrap: 'wrap' },
  classRow: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: MIN_TOUCH + 8 },
  badgeNum: { width: 36, height: 36, borderRadius: 10, backgroundColor: colors.tint, alignItems: 'center', justifyContent: 'center' },
  hwRow: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: MIN_TOUCH + 4 },
  leaveRow: { flexDirection: 'row', alignItems: 'center', gap: 12, borderRadius: radii.card },
});
