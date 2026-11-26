import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { Icon } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { formatMonthYear, formatShort, shiftMonth } from '../../lib/dates';
import { colors, MIN_TOUCH } from '../../theme/tokens';
import { useCalendar } from './queries';
import { monthWindow } from './rules';

/** The school calendar, one month at a time (read only). */
export function FamilyCalendar() {
  const now = new Date();
  const [ym, setYm] = useState({ year: now.getFullYear(), month: now.getMonth() + 1 });
  const { from, to } = monthWindow(ym.year, ym.month);
  const q = useCalendar(from, to);
  const list = q.data ?? [];
  return (
    <Screen title="Academic Calendar" onRefresh={() => void q.refetch()} refreshing={q.isRefetching}>
      <View style={styles.bar}>
        <Pressable onPress={() => setYm(shiftMonth(ym.year, ym.month, -1))} accessibilityRole="button" accessibilityLabel="Previous month" style={styles.nav}>
          <Icon name="chevron-left" size={22} color={colors.primaryText} />
        </Pressable>
        <Text variant="heading" weight="bold" color={colors.ink}>{formatMonthYear(ym.year, ym.month)}</Text>
        <Pressable onPress={() => setYm(shiftMonth(ym.year, ym.month, 1))} accessibilityRole="button" accessibilityLabel="Next month" style={styles.nav}>
          <Icon name="chevron-right" size={22} color={colors.primaryText} />
        </Pressable>
      </View>
      <QueryState
        loading={q.isPending}
        error={q.error}
        onRetry={() => void q.refetch()}
        empty={list.length === 0 ? { title: 'Nothing planned', message: 'Nothing is planned this month.', icon: 'calendar' } : null}
      >
        {list.map((e) => (
          <Card key={e.eventId} style={{ gap: 4 }}>
            <View style={styles.title}>
              <View style={[styles.dot, { backgroundColor: e.colorHex }]} />
              <Text variant="heading" weight="bold" color={colors.ink} style={{ flex: 1 }}>{e.title}</Text>
            </View>
            <Text variant="small">
              {formatShort(e.startDate)}{e.endDate !== e.startDate ? ` to ${formatShort(e.endDate)}` : ''} · {e.categoryName}
              {!e.isAllDay && e.startTime ? ` · ${e.startTime.slice(0, 5)}${e.endTime ? '–' + e.endTime.slice(0, 5) : ''}` : ''}
            </Text>
            {e.description ? <Text>{e.description}</Text> : null}
          </Card>
        ))}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  bar: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  nav: { minWidth: MIN_TOUCH, minHeight: MIN_TOUCH, alignItems: 'center', justifyContent: 'center' },
  title: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  dot: { width: 12, height: 12, borderRadius: 6 },
});
