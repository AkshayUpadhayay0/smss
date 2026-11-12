import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { Icon } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { formatFull } from '../../lib/dates';
import { classLabel } from '../../lib/format';
import { colors } from '../../theme/tokens';
import { useMySections } from './queries';

/** The sections this teacher can mark today (a teacher's own class-teacher sections). Opens the mark-attendance screen. */
export function TeacherAttendanceTab() {
  const router = useRouter();
  const sections = useMySections();
  const list = sections.data?.sections ?? [];
  return (
    <Screen title="Attendance" subtitle={sections.data ? formatFull(sections.data.today) : undefined} onRefresh={() => void sections.refetch()} refreshing={sections.isRefetching}>
      <QueryState
        loading={sections.isPending}
        error={sections.error}
        onRetry={() => void sections.refetch()}
        empty={list.length === 0 ? { title: 'No sections to mark', message: 'Only the class teacher of a section marks its attendance.', icon: 'check-square' } : null}
      >
        {list.map((s) => (
          <Pressable
            key={s.sectionId}
            onPress={() => router.push(`/(teacher)/mark-attendance/${s.sectionId}` as Href)}
            accessibilityRole="button"
            accessibilityLabel={`${classLabel(s.className, s.sectionName)}, ${s.rosterCount} students, ${s.markedToday ? 'marked today' : 'not marked today'}`}
          >
            <Card style={styles.card}>
              <View style={{ flex: 1, gap: 4 }}>
                <Text variant="heading">{classLabel(s.className, s.sectionName)}</Text>
                <Text variant="small">{s.rosterCount} {s.rosterCount === 1 ? 'student' : 'students'}</Text>
                {s.markedToday ? <Chip label="Marked today" tone="success" icon="check" /> : <Chip label="Not marked today" tone="warning" icon="alert-circle" />}
              </View>
              <Icon name="chevron-right" size={20} color={colors.muted} />
            </Card>
          </Pressable>
        ))}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  card: { flexDirection: 'row', alignItems: 'center', gap: 12 },
});
