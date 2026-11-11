import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { formatFull } from '../../lib/dates';
import { colors } from '../../theme/tokens';
import { useNotices } from './queries';

export function StudentNotices() {
  const router = useRouter();
  const notices = useNotices();
  const list = notices.data ?? []; // the API returns most recently published first
  const unread = list.filter((n) => !n.isRead).length;

  return (
    <Screen title="Notices" onRefresh={() => void notices.refetch()} refreshing={notices.isRefetching}>
      {unread > 0 ? <Chip label={`${unread} unread`} tone="primary" icon="bell" /> : null}
      <QueryState
        loading={notices.isPending}
        error={notices.error}
        onRetry={() => void notices.refetch()}
        empty={list.length === 0 ? { title: 'No notices', message: 'Notices from your school will show up here.', icon: 'bell-off' } : null}
      >
        {list.map((n) => (
          <Pressable
            key={n.noticeId}
            onPress={() => router.push(`/(student)/notice/${n.noticeId}` as Href)}
            accessibilityRole="button"
            accessibilityLabel={`${n.isRead ? '' : 'Unread. '}${n.title}. ${formatFull(n.publishedAt)}`}
          >
            <Card style={styles.card}>
              {!n.isRead ? <View style={styles.dot} /> : <View style={styles.dotSpace} />}
              <View style={{ flex: 1, gap: 4 }}>
                <Text variant="heading" weight={n.isRead ? 'medium' : 'bold'} color={colors.ink}>
                  {n.title}
                </Text>
                <Text variant="body" numberOfLines={2}>
                  {n.body}
                </Text>
                <Text variant="small">
                  {formatFull(n.publishedAt)}
                  {n.createdByName ? ` · ${n.createdByName}` : ''}
                </Text>
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
