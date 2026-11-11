import { StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { formatFull } from '../../lib/dates';
import { formatMarks } from '../../lib/format';
import { colors } from '../../theme/tokens';
import { useMarks } from './queries';

/** A student's own results, newest first. Deliberately NO average, rank, percentage or comparison anywhere (the API sends none either). */
export function StudentMarks() {
  const marks = useMarks();
  const list = [...(marks.data ?? [])].sort((a, b) => b.testDate.localeCompare(a.testDate) || b.assessmentId - a.assessmentId);
  return (
    <Screen title="Marks" onRefresh={() => void marks.refetch()} refreshing={marks.isRefetching}>
      <QueryState
        loading={marks.isPending}
        error={marks.error}
        onRetry={() => void marks.refetch()}
        empty={list.length === 0 ? { title: 'No marks yet', message: 'Test results will show up here.', icon: 'award' } : null}
      >
        {list.map((m) => (
          <Card key={m.assessmentId} style={styles.card}>
            <View style={styles.info}>
              <Text variant="label" color={colors.primaryText}>
                {m.subjectName}
              </Text>
              <Text variant="heading">{m.title}</Text>
              <Text variant="small">
                {m.assessmentTypeName} · {formatFull(m.testDate)}
              </Text>
            </View>
            {m.isAbsent || m.obtainedMarks === null ? (
              <Chip label="Absent" tone="danger" icon="user-x" />
            ) : (
              <Text variant="title" color={colors.ink} accessibilityLabel={`${formatMarks(m.obtainedMarks)} out of ${formatMarks(m.maxMarks)}`}>
                {formatMarks(m.obtainedMarks)} / {formatMarks(m.maxMarks)}
              </Text>
            )}
          </Card>
        ))}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  card: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  info: { flex: 1, gap: 2 },
});
