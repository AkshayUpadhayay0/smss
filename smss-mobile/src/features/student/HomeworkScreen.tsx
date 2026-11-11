import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Segmented } from '../../components/Segmented';
import { Text } from '../../components/Text';
import { diffDays, formatShort } from '../../lib/dates';
import { AttachmentButton, DueChip } from './parts';
import { useHomework, useServerToday } from './queries';

type Tab = 'upcoming' | 'past';

export function StudentHomework() {
  const [tab, setTab] = useState<Tab>('upcoming');
  const homework = useHomework();
  const { today, query: dashboard } = useServerToday();

  const all = homework.data ?? [];
  const list =
    today === undefined
      ? []
      : tab === 'upcoming'
        ? all.filter((h) => diffDays(h.dueDate, today) >= 0).sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.homeworkId - b.homeworkId)
        : all.filter((h) => diffDays(h.dueDate, today) < 0).sort((a, b) => b.dueDate.localeCompare(a.dueDate) || b.homeworkId - a.homeworkId);

  const refresh = () => {
    void homework.refetch();
    void dashboard.refetch();
  };

  return (
    <Screen title="Homework" onRefresh={refresh} refreshing={homework.isRefetching}>
      <Segmented
        value={tab}
        onChange={setTab}
        options={[
          { value: 'upcoming', label: 'Upcoming' },
          { value: 'past', label: 'Past' },
        ]}
      />
      <QueryState
        loading={homework.isPending || dashboard.isPending}
        error={homework.error ?? dashboard.error}
        onRetry={refresh}
        empty={list.length === 0 ? (tab === 'upcoming' ? { title: 'No homework due', message: 'You are all caught up.', icon: 'check-circle' } : { title: 'No past homework', icon: 'archive' }) : null}
      >
        {today
          ? list.map((h) => (
              <Card key={h.homeworkId} style={styles.card}>
                <View style={styles.chips}>
                  <Chip label={h.subjectName} tone="primary" />
                  <DueChip dueDate={h.dueDate} today={today} />
                </View>
                <Text variant="heading">{h.title}</Text>
                {h.description ? <Text variant="body">{h.description}</Text> : null}
                <Text variant="small">
                  {h.teacherName} · Assigned {formatShort(h.assignedDate)}
                </Text>
                {h.hasAttachment ? <AttachmentButton homeworkId={h.homeworkId} fileName={h.attachmentFileName} /> : null}
              </Card>
            ))
          : null}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  card: { gap: 8 },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
});
