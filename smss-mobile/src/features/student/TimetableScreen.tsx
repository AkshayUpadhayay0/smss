import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { apiDayOfWeek, formatTime, WEEKDAYS_LONG, WEEKDAYS_SHORT } from '../../lib/dates';
import { colors, MIN_TOUCH, radii } from '../../theme/tokens';
import { useServerToday, useTimetable } from './queries';

const DAYS = [1, 2, 3, 4, 5, 6]; // Monday .. Saturday, as the API numbers them

export function StudentTimetable() {
  const timetable = useTimetable();
  const { today, query: dashboard } = useServerToday();
  const [picked, setPicked] = useState<number | null>(null);

  // Today's weekday comes from the SERVER's date. On a Sunday (no school day) start on Monday.
  const todayDow = today ? apiDayOfWeek(today) : null;
  const selected = picked ?? (todayDow !== null && todayDow <= 6 ? todayDow : 1);

  const rows = [...(timetable.data?.rows ?? [])].sort((a, b) => a.period.periodNumber - b.period.periodNumber);
  const dayRows = rows.map((r) => ({ period: r.period, entry: r.cells.find((c) => c.dayOfWeek === selected)?.entry ?? null }));
  const hasAny = dayRows.some((r) => r.entry !== null);
  const refresh = () => {
    void timetable.refetch();
    void dashboard.refetch();
  };

  return (
    <Screen title="Timetable" onRefresh={refresh} refreshing={timetable.isRefetching}>
      <View style={styles.days} accessibilityRole="tablist">
        {DAYS.map((d) => {
          const isSel = d === selected;
          const isToday = d === todayDow;
          return (
            <Pressable
              key={d}
              onPress={() => setPicked(d)}
              accessibilityRole="tab"
              accessibilityState={{ selected: isSel }}
              accessibilityLabel={`${WEEKDAYS_LONG[d - 1]}${isToday ? ', today' : ''}`}
              style={[styles.day, isSel && styles.daySelected, !isSel && isToday && styles.dayToday]}
            >
              <Text variant="label" color={isSel ? colors.onPrimary : isToday ? colors.primaryText : colors.inkSoft} weight={isSel ? 'bold' : 'semibold'}>
                {WEEKDAYS_SHORT[d - 1]}
              </Text>
            </Pressable>
          );
        })}
      </View>

      <QueryState
        loading={timetable.isPending || dashboard.isPending}
        error={timetable.error ?? dashboard.error}
        onRetry={refresh}
        empty={!hasAny ? { title: `No classes on ${WEEKDAYS_LONG[selected - 1]}`, message: 'Nothing is scheduled for this day.', icon: 'calendar' } : null}
      >
        <Card style={{ gap: 4 }}>
          {dayRows.map(({ period, entry }) => (
            <View key={period.periodId} style={styles.row} accessible accessibilityLabel={entry ? `Period ${period.periodNumber}, ${entry.subjectName}, ${entry.employeeName}, ${formatTime(period.startTime)} to ${formatTime(period.endTime)}` : `Period ${period.periodNumber}, no class`}>
              <View style={[styles.num, !entry && { backgroundColor: colors.divider }]}>
                <Text variant="label" weight="bold" color={entry ? colors.primaryText : colors.muted}>
                  {period.periodNumber}
                </Text>
              </View>
              <View style={{ flex: 1 }}>
                {entry ? (
                  <>
                    <Text variant="heading" style={{ fontSize: 15 }}>
                      {entry.subjectName}
                    </Text>
                    <Text variant="small">
                      {entry.employeeName}
                      {entry.roomNo ? ` · Room ${entry.roomNo}` : ''}
                    </Text>
                  </>
                ) : (
                  <Text variant="small">No class</Text>
                )}
              </View>
              <Text variant="small" style={{ textAlign: 'right' }}>
                {formatTime(period.startTime)}
                {'\n'}
                {formatTime(period.endTime)}
              </Text>
            </View>
          ))}
        </Card>
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  days: { flexDirection: 'row', gap: 6 },
  day: { flex: 1, minHeight: MIN_TOUCH, borderRadius: radii.chip, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border },
  daySelected: { backgroundColor: colors.primary, borderColor: colors.primary },
  dayToday: { borderColor: colors.primary, borderWidth: 2 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: MIN_TOUCH + 12, paddingVertical: 6 },
  num: { width: 36, height: 36, borderRadius: 10, backgroundColor: colors.tint, alignItems: 'center', justifyContent: 'center' },
});
