import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { Avatar } from '../../components/Avatar';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { Icon } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { colors } from '../../theme/tokens';
import { cardChip, classLabel, rupees, unreadBadge } from './rules';
import { useChildren, useUnread } from './queries';

/** The parent's home: one card per linked child (even with one), the fee tile, and the Notices and Calendar buttons. Reads only /api/Family. */
export function FamilyHome() {
  const router = useRouter();
  const children = useChildren();
  const unread = useUnread();
  const data = children.data;
  const badge = unreadBadge(unread.data);

  return (
    <Screen title="Your family" onRefresh={() => { void children.refetch(); void unread.refetch(); }} refreshing={children.isRefetching}>
      <QueryState
        loading={children.isPending}
        error={children.error}
        onRetry={() => void children.refetch()}
        empty={data && data.children.length === 0 ? { title: 'No children linked', message: 'No children are linked to your account yet. If this is not right, please contact the school.', icon: 'users' } : null}
      >
        {data ? (
          <>
            <Card style={[styles.tile, data.feeTile.allPaid ? styles.paid : styles.due]} accessibilityLabel={data.feeTile.allPaid ? 'Fees: paid' : `Fees due now ${rupees(data.feeTile.totalDueNow)}`}>
              {data.feeTile.allPaid ? (
                <>
                  <Text variant="heading" weight="bold" color={colors.ink}>Fees: Paid</Text>
                  <Text variant="small">Nothing is due right now for any of your children.</Text>
                </>
              ) : (
                <>
                  <Text variant="heading" weight="bold" color={colors.ink}>Fees due now: {rupees(data.feeTile.totalDueNow)}</Text>
                  {(data.feeTile.totalEarlierYears ?? 0) > 0 ? <Text variant="body" weight="bold">Outstanding from earlier years: {rupees(data.feeTile.totalEarlierYears ?? 0)}</Text> : null}
                  {data.feeTile.children.map((c) => (
                    <Pressable key={c.studentId} onPress={() => router.push({ pathname: '/(family)/child/[studentId]', params: { studentId: c.studentId, tab: 'fees' } } as unknown as Href)} accessibilityRole="button" accessibilityLabel={`${c.fullName}, ${rupees(c.dueNow)} due`}>
                      <Text variant="body">{c.fullName}: {rupees(c.dueNow)}{(c.earlierYears ?? 0) > 0 ? ` + ${rupees(c.earlierYears ?? 0)} from earlier years` : ''}</Text>
                    </Pressable>
                  ))}
                </>
              )}
            </Card>

            {data.children.map((c) => {
              const chip = cardChip(c);
              return (
                <Pressable
                  key={c.studentId}
                  onPress={() => router.push({ pathname: '/(family)/child/[studentId]', params: { studentId: c.studentId } } as unknown as Href)}
                  accessibilityRole="button"
                  accessibilityLabel={`${c.fullName}, ${classLabel(c)}, ${chip.label}`}
                >
                  <Card style={[styles.card, c.readOnly && styles.readOnly]}>
                    <Avatar name={c.fullName} size={52} />
                    <View style={styles.who}>
                      <Text variant="heading" weight="bold" color={colors.ink}>{c.fullName}</Text>
                      <Text variant="small">{c.studentId} · {classLabel(c)}</Text>
                      <View style={styles.chips}>
                        <Chip label={chip.label} tone={chip.tone} />
                        {c.feeDueNow > 0 ? <Chip label={`Fee due ${rupees(c.feeDueNow)}`} tone="danger" /> : null}
                        {(c.feeEarlierYears ?? 0) > 0 ? <Chip label={`Earlier years ${rupees(c.feeEarlierYears ?? 0)}`} tone="danger" /> : null}
                      </View>
                    </View>
                    <Icon name="chevron-right" size={20} color={colors.muted} />
                  </Card>
                </Pressable>
              );
            })}
          </>
        ) : null}
      </QueryState>

      <View style={styles.actions}>
        <Pressable style={styles.action} onPress={() => router.push('/(family)/(tabs)/notices' as Href)} accessibilityRole="button" accessibilityLabel={badge ? `School Notifications, ${badge} unread` : 'School Notifications'}>
          <Icon name="bell" size={20} color={colors.onPrimary} />
          <Text weight="bold" color={colors.onPrimary}>School Notifications</Text>
          {badge ? <View style={styles.badge}><Text variant="small" weight="bold" color={colors.danger}>{badge}</Text></View> : null}
        </Pressable>
        <Pressable style={styles.action} onPress={() => router.push('/(family)/(tabs)/calendar' as Href)} accessibilityRole="button" accessibilityLabel="Academic Calendar">
          <Icon name="calendar" size={20} color={colors.onPrimary} />
          <Text weight="bold" color={colors.onPrimary}>Academic Calendar</Text>
        </Pressable>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  tile: { gap: 4 },
  paid: { borderColor: colors.success },
  due: { borderColor: colors.danger },
  card: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  readOnly: { opacity: 0.85 },
  who: { flex: 1, gap: 2 },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: 6, marginTop: 4 },
  actions: { gap: 10, marginTop: 4 },
  action: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 8, minHeight: 52, borderRadius: 14, backgroundColor: colors.primary, paddingHorizontal: 16 },
  badge: { minWidth: 22, paddingHorizontal: 6, borderRadius: 11, backgroundColor: colors.surface, alignItems: 'center' },
});
