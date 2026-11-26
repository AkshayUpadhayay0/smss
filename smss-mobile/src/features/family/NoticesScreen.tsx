import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { getNotice } from '../../api/family';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { formatFull } from '../../lib/dates';
import { colors } from '../../theme/tokens';
import { familyKeys, useNotices } from './queries';

/** School notices for parents. Opening one marks it read (the server does that on the GET); the unread badge and the list are refreshed afterwards. */
export function FamilyNotices() {
  const qc = useQueryClient();
  const notices = useNotices();
  const [openId, setOpenId] = useState<number | null>(null);
  const list = notices.data ?? [];
  const unread = list.filter((n) => !n.isRead).length;

  async function toggle(id: number, isRead: boolean) {
    if (openId === id) {
      setOpenId(null);
      return;
    }
    setOpenId(id);
    if (!isRead) {
      try {
        await getNotice(id);
        await Promise.all([qc.invalidateQueries({ queryKey: familyKeys.notices }), qc.invalidateQueries({ queryKey: familyKeys.unread })]);
      } catch {
        // the text is already on screen; the unread mark simply stays until the next refresh
      }
    }
  }

  return (
    <Screen title="School Notifications" onRefresh={() => void notices.refetch()} refreshing={notices.isRefetching}>
      {unread > 0 ? <Chip label={`${unread} unread`} tone="primary" icon="bell" /> : null}
      <QueryState
        loading={notices.isPending}
        error={notices.error}
        onRetry={() => void notices.refetch()}
        empty={list.length === 0 ? { title: 'No notices', message: 'Notices from your school will show up here.', icon: 'bell' } : null}
      >
        {list.map((n) => (
          <Pressable
            key={n.noticeId}
            onPress={() => void toggle(n.noticeId, n.isRead)}
            accessibilityRole="button"
            accessibilityState={{ expanded: openId === n.noticeId }}
            accessibilityLabel={`${n.isRead ? '' : 'Unread. '}${n.title}. ${formatFull(n.publishedAt.slice(0, 10))}`}
          >
            <Card style={styles.card}>
              <View style={n.isRead ? styles.dotSpace : styles.dot} />
              <View style={{ flex: 1, gap: 4 }}>
                <Text variant="heading" weight={n.isRead ? 'medium' : 'bold'} color={colors.ink}>{n.title}</Text>
                <Text variant="small">{formatFull(n.publishedAt.slice(0, 10))}{n.postedBy ? ` · ${n.postedBy}` : ''}</Text>
                {openId === n.noticeId ? <Text>{n.body}</Text> : null}
              </View>
            </Card>
          </Pressable>
        ))}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  card: { flexDirection: 'row', gap: 10 },
  dot: { width: 10, height: 10, borderRadius: 5, backgroundColor: colors.primary, marginTop: 6 },
  dotSpace: { width: 10 },
});
