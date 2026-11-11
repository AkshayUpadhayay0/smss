import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useEffect } from 'react';
import { Card } from '../../components/Card';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { getNotice } from '../../api/student';
import { formatFull } from '../../lib/dates';
import { studentKeys } from './queries';

/**
 * Opening a notice is ONE call: GET /api/Notice/{id}. The API marks it read as part of that GET, so there is no separate mark-read request.
 * Afterwards the list, the bell count and the dashboard are refreshed. gcTime/staleTime 0: every open really hits the server.
 */
export function NoticeDetail() {
  const router = useRouter();
  const client = useQueryClient();
  const { id } = useLocalSearchParams<{ id: string }>();
  const noticeId = Number(id);

  const notice = useQuery({
    queryKey: ['student', 'notice', noticeId],
    queryFn: () => getNotice(noticeId),
    enabled: Number.isInteger(noticeId) && noticeId > 0,
    staleTime: 0,
    gcTime: 0,
  });

  const loaded = notice.isSuccess;
  useEffect(() => {
    if (!loaded) return;
    void client.invalidateQueries({ queryKey: studentKeys.notices });
    void client.invalidateQueries({ queryKey: studentKeys.unread });
    void client.invalidateQueries({ queryKey: studentKeys.dashboard });
  }, [loaded, client]);

  const n = notice.data;
  return (
    <Screen title="Notice" onBack={() => router.back()} edges={['top', 'left', 'right', 'bottom']} onRefresh={() => void notice.refetch()} refreshing={notice.isRefetching}>
      <QueryState loading={notice.isPending && notice.fetchStatus !== 'idle'} error={notice.error} onRetry={() => void notice.refetch()} skeletons={1}>
        {n ? (
          <Card style={{ gap: 8 }}>
            <Text variant="title">{n.title}</Text>
            <Text variant="small">
              {formatFull(n.publishedAt)}
              {n.createdByName ? ` · ${n.createdByName}` : ''}
            </Text>
            <Text variant="body" selectable>
              {n.body}
            </Text>
          </Card>
        ) : null}
      </QueryState>
    </Screen>
  );
}
