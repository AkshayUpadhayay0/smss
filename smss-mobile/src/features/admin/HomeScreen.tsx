import { StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { HomeHeader } from '../../components/HomeHeader';
import { Icon, type IconName } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { colors } from '../../theme/tokens';
import { SectionHeader } from '../student/parts';
import { admissionsCard, attendanceCard, employeeLines, feeCards, isEmptySchool, moneyCards, showVehicles, type AdminCard, type CardTone } from './derive';
import { useSchoolAdminDashboard } from './queries';

const BAR: Record<CardTone, string> = {
  primary: colors.primary,
  success: colors.success,
  danger: colors.danger,
  warning: colors.warningText,
  info: colors.primaryText,
};

const ICONS: Record<string, IconName> = {
  'total-students': 'users',
  paid: 'check-circle',
  'not-paid': 'alert-circle',
  collected: 'credit-card',
  outstanding: 'clock',
  'income-month': 'trending-up',
  'income-session': 'trending-up',
  'expenses-month': 'trending-down',
  'expenses-session': 'trending-down',
  attendance: 'calendar',
  'new-admissions': 'user-plus',
};

function StatCard({ card }: { card: AdminCard }) {
  const bar = BAR[card.tone];
  return (
    <Card style={[styles.card, { borderLeftColor: bar }, card.muted && styles.muted]} accessible accessibilityLabel={`${card.label}: ${card.value}. ${card.note}`}>
      <Icon name={ICONS[card.id] ?? 'info'} size={20} color={bar} />
      <View style={styles.text}>
        <Text variant="small">{card.label}</Text>
        <Text variant={card.muted ? 'label' : 'title'} color={card.muted ? colors.muted : colors.ink}>
          {card.value}
        </Text>
        <Text variant="small">{card.note}</Text>
      </View>
    </Card>
  );
}

/** The School Admin's home: the same figures as the web dashboard (GET /api/Dashboard/school-admin), stacked for a phone. */
export function AdminHome() {
  const dashboard = useSchoolAdminDashboard();
  const d = dashboard.data;
  const refresh = () => void dashboard.refetch();

  return (
    <Screen onRefresh={refresh} refreshing={dashboard.isRefetching}>
      <HomeHeader subtitle="School Admin" />
      <QueryState loading={dashboard.isPending} error={dashboard.error} onRetry={refresh} skeletons={4}>
        {d ? (
          <>
            {isEmptySchool(d) ? (
              <Card>
                <Text variant="small">No students or staff have been added yet, so the figures below are zero. They fill in as you add students, employees and fees.</Text>
              </Card>
            ) : null}

            <SectionHeader title="Students & Fees" />
            {feeCards(d).map((c) => (
              <StatCard key={c.id} card={c} />
            ))}

            <SectionHeader title="Money" />
            {moneyCards(d).map((c) => (
              <StatCard key={c.id} card={c} />
            ))}

            <SectionHeader title="People" />
            <StatCard card={attendanceCard(d)} />
            <StatCard card={admissionsCard(d)} />
            <Card style={[styles.card, { borderLeftColor: colors.primaryText }]} accessible accessibilityLabel={`Employees: ${d.employees.total}`}>
              <Icon name="users" size={20} color={colors.primaryText} />
              <View style={styles.text}>
                <Text variant="small">Employees</Text>
                <Text variant="title">{d.employees.total}</Text>
                {employeeLines(d).map((l) => (
                  <View key={l.label} style={styles.line}>
                    <Text variant="small">{l.label}</Text>
                    <Text variant="label">{l.count}</Text>
                  </View>
                ))}
              </View>
            </Card>
            {showVehicles(d) ? (
              <Card style={[styles.card, { borderLeftColor: colors.warningText }]} accessible accessibilityLabel={`Vehicles: ${d.activeVehicles}`}>
                <Icon name="truck" size={20} color={colors.warningText} />
                <View style={styles.text}>
                  <Text variant="small">Vehicles</Text>
                  <Text variant="title">{d.activeVehicles}</Text>
                  <Text variant="small">Active vehicles</Text>
                </View>
              </Card>
            ) : null}
          </>
        ) : null}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  card: { flexDirection: 'row', gap: 12, borderLeftWidth: 4, alignItems: 'flex-start' },
  muted: { backgroundColor: colors.background },
  text: { flex: 1, gap: 2 },
  line: { flexDirection: 'row', justifyContent: 'space-between', marginTop: 2 },
});
