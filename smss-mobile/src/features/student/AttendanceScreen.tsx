import { useRouter } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { Icon } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { daysInMonth, formatDayLong, formatMonthYear, monthKey, monthRange, parts, shiftMonth, WEEKDAYS_SHORT, weekdayMonFirst, ymd } from '../../lib/dates';
import { colors, MIN_TOUCH, radii } from '../../theme/tokens';
import { useDashboard, useOwnAttendance } from './queries';

const PRESENT = 'PRESENT';
const ABSENT = 'ABSENT';

function Tile({ label, value, tone }: { label: string; value: string; tone: 'success' | 'danger' | 'primary' }) {
  const color = tone === 'success' ? colors.success : tone === 'danger' ? colors.danger : colors.primaryText;
  const bg = tone === 'success' ? colors.successTint : tone === 'danger' ? colors.dangerTint : colors.tint;
  return (
    <View style={[styles.tile, { backgroundColor: bg }]} accessible accessibilityLabel={`${label}: ${value}`}>
      <Text variant="title" color={color}>
        {value}
      </Text>
      <Text variant="small" color={color} weight="semibold">
        {label}
      </Text>
    </View>
  );
}

function Legend() {
  return (
    <View style={styles.legend}>
      <View style={styles.legendItem}>
        <View style={[styles.swatch, { backgroundColor: colors.successTint, borderColor: colors.successBorder }]}>
          <Icon name="check" size={11} color={colors.success} />
        </View>
        <Text variant="small">Present</Text>
      </View>
      <View style={styles.legendItem}>
        <View style={[styles.swatch, { backgroundColor: colors.danger, borderColor: colors.danger }]}>
          <Text variant="tab" color={colors.onPrimary} weight="bold" style={{ fontSize: 10, lineHeight: 12 }}>
            A
          </Text>
        </View>
        <Text variant="small">Absent</Text>
      </View>
      <View style={styles.legendItem}>
        <View style={[styles.swatch, { backgroundColor: colors.surface, borderColor: colors.primary, borderWidth: 2 }]} />
        <Text variant="small">Today</Text>
      </View>
    </View>
  );
}

export function AttendanceScreen() {
  const router = useRouter();
  const dashboard = useDashboard();
  const today = dashboard.data?.today;
  const yearStart = dashboard.data?.attendance?.from;

  // The month is an offset from the SERVER's current month (never the phone's clock); it can only go back, not forward.
  const [offset, setOffset] = useState(0);
  const base = today ? parts(today) : null;
  const shown = base ? shiftMonth(base.year, base.month, offset) : null;
  const range = shown ? monthRange(shown.year, shown.month) : null;
  const attendance = useOwnAttendance(range?.from ?? '', range?.to ?? '', !!range);

  const canGoNext = offset < 0;
  const startKey = yearStart ? monthKey(parts(yearStart).year, parts(yearStart).month) : null;
  const canGoPrev = shown ? startKey === null || monthKey(shown.year, shown.month) > startKey : false;

  const byDate = new Map((attendance.data?.days ?? []).map((d) => [d.date, d.statusCode.toUpperCase()]));
  const present = attendance.data?.summary.byStatus[PRESENT] ?? 0;
  const absent = attendance.data?.summary.byStatus[ABSENT] ?? 0;
  const rate = attendance.data?.summary.presentPercentage;

  const cells: (number | null)[] = [];
  if (shown) {
    const lead = weekdayMonFirst(ymd(shown.year, shown.month, 1));
    for (let i = 0; i < lead; i++) cells.push(null);
    for (let d = 1; d <= daysInMonth(shown.year, shown.month); d++) cells.push(d);
    while (cells.length % 7 !== 0) cells.push(null);
  }
  const weeks: (number | null)[][] = [];
  for (let i = 0; i < cells.length; i += 7) weeks.push(cells.slice(i, i + 7));

  const loading = dashboard.isPending || (!!range && attendance.isPending);
  const error = dashboard.error ?? attendance.error;
  const retry = () => {
    void dashboard.refetch();
    void attendance.refetch();
  };

  return (
    <Screen title="Attendance" onBack={() => router.back()} edges={['top', 'left', 'right', 'bottom']} onRefresh={retry} refreshing={attendance.isRefetching}>
      <QueryState loading={loading} error={error} onRetry={retry}>
        {shown && today ? (
          <>
            <View style={styles.switcher}>
              <Pressable
                onPress={() => setOffset((o) => o - 1)}
                disabled={!canGoPrev}
                accessibilityRole="button"
                accessibilityLabel="Previous month"
                accessibilityState={{ disabled: !canGoPrev }}
                style={[styles.nav, !canGoPrev && { opacity: 0.35 }]}
              >
                <Icon name="chevron-left" size={22} color={colors.ink} />
              </Pressable>
              <Text variant="heading" accessibilityRole="header" accessibilityLiveRegion="polite">
                {formatMonthYear(shown.year, shown.month)}
              </Text>
              <Pressable
                onPress={() => setOffset((o) => o + 1)}
                disabled={!canGoNext}
                accessibilityRole="button"
                accessibilityLabel="Next month"
                accessibilityState={{ disabled: !canGoNext }}
                style={[styles.nav, !canGoNext && { opacity: 0.35 }]}
              >
                <Icon name="chevron-right" size={22} color={colors.ink} />
              </Pressable>
            </View>

            <Text variant="small" style={{ marginBottom: -4 }}>
              {formatMonthYear(shown.year, shown.month)} only. The Home screen shows the whole academic year.
            </Text>
            <View style={styles.tiles}>
              <Tile label="Present" value={String(present)} tone="success" />
              <Tile label="Absent" value={String(absent)} tone="danger" />
              <Tile label="Month rate" value={rate === null || rate === undefined ? '—' : `${rate}%`} tone="primary" />
            </View>

            <Card style={{ gap: 8 }}>
              <View style={styles.week}>
                {WEEKDAYS_SHORT.map((w) => (
                  <Text key={w} variant="small" weight="semibold" style={styles.weekday}>
                    {w}
                  </Text>
                ))}
              </View>
              {weeks.map((week, wi) => (
                <View key={wi} style={styles.week}>
                  {week.map((day, di) => {
                    if (day === null) return <View key={di} style={styles.cell} />;
                    const date = ymd(shown.year, shown.month, day);
                    const status = byDate.get(date);
                    const isToday = date === today;
                    const label = `${formatDayLong(date)}${isToday ? ', today' : ''}${status === PRESENT ? ', present' : status === ABSENT ? ', absent' : ''}`;
                    return (
                      <View key={di} style={styles.cell} accessible accessibilityLabel={label}>
                        <View
                          style={[
                            styles.day,
                            status === PRESENT && { backgroundColor: colors.successTint, borderColor: colors.successBorder },
                            status === ABSENT && { backgroundColor: colors.danger, borderColor: colors.danger },
                            isToday && { borderColor: colors.primary, borderWidth: 2 },
                          ]}
                        >
                          <Text variant="small" weight="semibold" color={status === ABSENT ? colors.onPrimary : status === PRESENT ? colors.success : colors.muted} style={{ fontSize: 12, lineHeight: 14 }}>
                            {day}
                          </Text>
                          {status === PRESENT ? <Icon name="check" size={13} color={colors.success} /> : null}
                          {status === ABSENT ? (
                            <Text variant="tab" weight="bold" color={colors.onPrimary} style={{ fontSize: 12, lineHeight: 14 }}>
                              A
                            </Text>
                          ) : null}
                        </View>
                      </View>
                    );
                  })}
                </View>
              ))}
              <Legend />
            </Card>

            <Text variant="small" style={{ textAlign: 'center' }}>
              Attendance is marked by your class teacher.
            </Text>
          </>
        ) : null}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  switcher: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  nav: { width: MIN_TOUCH, height: MIN_TOUCH, borderRadius: MIN_TOUCH / 2, backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border, alignItems: 'center', justifyContent: 'center' },
  tiles: { flexDirection: 'row', gap: 10 },
  tile: { flex: 1, borderRadius: radii.card, padding: 12, alignItems: 'center', gap: 2 },
  week: { flexDirection: 'row' },
  weekday: { flex: 1, textAlign: 'center' },
  cell: { flex: 1, aspectRatio: 0.9, padding: 2 },
  day: { flex: 1, borderRadius: radii.chipSmall, borderWidth: 1, borderColor: 'transparent', alignItems: 'center', justifyContent: 'center', gap: 1 },
  legend: { flexDirection: 'row', justifyContent: 'center', gap: 20, paddingTop: 8 },
  legendItem: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  swatch: { width: 20, height: 20, borderRadius: 6, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
});
