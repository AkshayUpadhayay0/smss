import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { messageOf } from '../../api/errors';
import { decideLeave, type StudentLeaveRequest } from '../../api/teacher';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { Icon } from '../../components/Icon';
import { Input } from '../../components/Input';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Segmented } from '../../components/Segmented';
import { Text } from '../../components/Text';
import { formatShort } from '../../lib/dates';
import { classLabel } from '../../lib/format';
import { colors, radii } from '../../theme/tokens';
import { LeaveStatusChip } from '../student/parts';
import { splitLeave } from './derive';
import { teacherKeys, useStudentLeave } from './queries';
import { LEAVE_REASON_MAX } from './rules';

function dates(l: StudentLeaveRequest): string {
  return l.fromDate === l.toDate ? formatShort(l.fromDate) : `${formatShort(l.fromDate)} – ${formatShort(l.toDate)}`;
}

function LeaveCard({ leave }: { leave: StudentLeaveRequest }) {
  const client = useQueryClient();
  const pending = leave.status.trim().toLowerCase() === 'pending';
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string | null>(null);

  const decide = useMutation({
    mutationFn: (body: { decision: 'Approve' | 'Reject'; decisionReason?: string }) => decideLeave(leave.leaveRequestId, body),
    onSuccess: () => {
      setRejecting(false);
      void client.invalidateQueries({ queryKey: teacherKeys.leave });
    },
  });

  function confirmReject() {
    const text = reason.trim();
    if (!text) {
      setReasonError('A reason is required to reject a request.');
      return;
    }
    setReasonError(null);
    decide.mutate({ decision: 'Reject', decisionReason: text });
  }

  return (
    <Card style={{ gap: 8 }}>
      <View style={styles.between}>
        <View style={{ flex: 1 }}>
          <Text variant="heading">{leave.studentName}</Text>
          <Text variant="small">{classLabel(leave.className, leave.sectionName)}</Text>
        </View>
        <LeaveStatusChip status={leave.status} />
      </View>
      <View style={styles.chips}>
        <Chip label={leave.leaveTypeName} tone="primary" />
        <Chip label={`${dates(leave)} · ${leave.days} day${leave.days === 1 ? '' : 's'}`} tone="neutral" icon="calendar" />
      </View>
      <Text variant="body">{leave.reason}</Text>

      {!pending && (leave.decisionReason || leave.decidedByName) ? (
        <Text variant="small">
          {leave.decidedByName ? `Decided by ${leave.decidedByName}` : 'Decided'}
          {leave.decisionReason ? `: ${leave.decisionReason}` : ''}
        </Text>
      ) : null}

      {decide.isError ? (
        <View style={styles.error} accessibilityRole="alert" accessibilityLiveRegion="assertive">
          <Icon name="alert-circle" size={18} color={colors.danger} />
          <Text variant="small" color={colors.danger} style={{ flex: 1 }}>
            {messageOf(decide.error)}
          </Text>
        </View>
      ) : null}

      {pending && !rejecting ? (
        <View style={styles.actions}>
          <Button title="Approve" icon="check" onPress={() => decide.mutate({ decision: 'Approve' })} loading={decide.isPending} style={{ flex: 1 }} />
          <Button title="Reject" icon="x" variant="danger" onPress={() => setRejecting(true)} disabled={decide.isPending} style={{ flex: 1 }} />
        </View>
      ) : null}

      {pending && rejecting ? (
        <View style={{ gap: 10 }}>
          <Input
            label="Reason for rejecting (required)"
            value={reason}
            onChangeText={(t) => {
              setReason(t);
              if (reasonError) setReasonError(null);
            }}
            multiline
            maxLength={LEAVE_REASON_MAX}
            showCount
            error={reasonError}
            editable={!decide.isPending}
          />
          <View style={styles.actions}>
            <Button title="Confirm reject" icon="x" variant="danger" onPress={confirmReject} loading={decide.isPending} style={{ flex: 1 }} />
            <Button
              title="Cancel"
              variant="secondary"
              onPress={() => {
                setRejecting(false);
                setReason('');
                setReasonError(null);
              }}
              disabled={decide.isPending}
              style={{ flex: 1 }}
            />
          </View>
        </View>
      ) : null}
    </Card>
  );
}

type Tab = 'pending' | 'decided';

/** Student leave for the sections this teacher is class teacher of. ONE request without a status filter, split into Pending / Decided here. */
export function TeacherLeave() {
  const [tab, setTab] = useState<Tab>('pending');
  const leave = useStudentLeave();
  const { pending, decided } = splitLeave(leave.data ?? []);
  const list = tab === 'pending' ? pending : decided;
  return (
    <Screen title="Student leave" onRefresh={() => void leave.refetch()} refreshing={leave.isRefetching}>
      <Segmented
        value={tab}
        onChange={setTab}
        options={[
          { value: 'pending', label: `Pending${pending.length ? ` (${pending.length})` : ''}` },
          { value: 'decided', label: 'Decided' },
        ]}
      />
      <QueryState
        loading={leave.isPending}
        error={leave.error}
        onRetry={() => void leave.refetch()}
        empty={
          list.length === 0
            ? tab === 'pending'
              ? { title: 'No requests waiting', message: 'Leave requests from the students of your own class show up here.', icon: 'check-circle' }
              : { title: 'Nothing decided yet', icon: 'archive' }
            : null
        }
      >
        {list.map((l) => (
          <LeaveCard key={l.leaveRequestId} leave={l} />
        ))}
      </QueryState>
    </Screen>
  );
}

const styles = StyleSheet.create({
  between: { flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: 8 },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  actions: { flexDirection: 'row', gap: 10 },
  error: { flexDirection: 'row', alignItems: 'flex-start', gap: 8, padding: 12, borderRadius: radii.input, backgroundColor: colors.dangerTint },
});
