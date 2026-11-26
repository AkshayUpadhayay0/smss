import { useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import type { FamilyChild } from '../../api/family';
import { messageOf } from '../../api/errors';
import { Avatar } from '../../components/Avatar';
import { Card } from '../../components/Card';
import { Chip } from '../../components/Chip';
import { ErrorState } from '../../components/ErrorState';
import { Icon } from '../../components/Icon';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Text } from '../../components/Text';
import { formatFull, formatShort } from '../../lib/dates';
import { openReportCardPdf } from '../../lib/attachments';
import { colors, MIN_TOUCH } from '../../theme/tokens';
import { useAttendance, useChildren, useExams, useFees, useHomework, useLeave, useProfile, useReceipt, useReportCards, useTimetable } from './queries';
import { CHILD_TABS, classLabel, lastThirtyDays, readOnlyBanner, rupees, type ChildTab } from './rules';

/** One child's read-only dashboard: a header with a child switcher, the read-only banner, and a tab per area. A tab asks the server only while it is open. */
export function ChildDashboard({ studentId, initialTab }: { studentId: string; initialTab?: string }) {
  const router = useRouter();
  const children = useChildren();
  const [tab, setTab] = useState<ChildTab>(CHILD_TABS.find((t) => t.value === initialTab)?.value ?? 'profile');
  const list = children.data?.children ?? [];
  const child = list.find((c) => c.studentId === studentId) ?? null;
  const banner = readOnlyBanner(child?.chipState);

  return (
    <Screen title={child?.fullName ?? 'Child'} onBack={() => router.back()} onRefresh={() => void children.refetch()} refreshing={children.isRefetching}>
      <QueryState loading={children.isPending} error={children.error} onRetry={() => void children.refetch()} empty={!children.isPending && !children.error && !child ? { title: 'Child not found', message: 'This child could not be found on your account.', icon: 'user-x' } : null}>
        {child ? (
          <>
            <Card style={styles.head}>
              <Avatar name={child.fullName} size={48} />
              <View style={{ flex: 1 }}>
                <Text variant="heading" weight="bold" color={colors.ink}>{child.fullName}</Text>
                <Text variant="small">{child.studentId} · {classLabel(child)}</Text>
              </View>
            </Card>
            {list.length > 1 ? <Switcher list={list} current={child.studentId} onPick={(id) => router.replace({ pathname: '/(family)/child/[studentId]', params: { studentId: id, tab } } as unknown as Href)} /> : null}
            {banner ? (
              <Card style={styles.banner} accessibilityRole="alert">
                <Icon name="info" size={18} color={colors.warningText} />
                <Text variant="small" color={colors.warningText} style={{ flex: 1 }}>{banner}</Text>
              </Card>
            ) : null}
            <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.tabs} accessibilityRole="tablist">
              {CHILD_TABS.map((t) => (
                <Pressable key={t.value} onPress={() => setTab(t.value)} accessibilityRole="tab" accessibilityState={{ selected: tab === t.value }} style={[styles.tab, tab === t.value && styles.tabOn]}>
                  <Text variant="label" weight={tab === t.value ? 'bold' : 'semibold'} color={tab === t.value ? colors.onPrimary : colors.inkSoft}>{t.label}</Text>
                </Pressable>
              ))}
            </ScrollView>
            <TabBody tab={tab} id={child.studentId} />
          </>
        ) : null}
      </QueryState>
    </Screen>
  );
}

function Switcher({ list, current, onPick }: { list: FamilyChild[]; current: string; onPick: (id: string) => void }) {
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.tabs} accessibilityLabel="Switch child">
      {list.map((c) => (
        <Pressable key={c.studentId} onPress={() => onPick(c.studentId)} accessibilityRole="button" accessibilityState={{ selected: c.studentId === current }} accessibilityLabel={`Show ${c.fullName}`} style={[styles.tab, c.studentId === current && styles.tabOn]}>
          <Text variant="label" weight="semibold" color={c.studentId === current ? colors.onPrimary : colors.inkSoft}>{c.fullName.split(' ')[0]}</Text>
        </Pressable>
      ))}
    </ScrollView>
  );
}

function TabBody({ tab, id }: { tab: ChildTab; id: string }) {
  switch (tab) {
    case 'profile': return <ProfileTab id={id} />;
    case 'attendance': return <AttendanceTab id={id} />;
    case 'homework': return <HomeworkTab id={id} />;
    case 'timetable': return <TimetableTab id={id} />;
    case 'exams': return <ExamsTab id={id} />;
    case 'reports': return <ReportsTab id={id} />;
    case 'fees': return <FeesTab id={id} />;
    default: return <LeaveTab id={id} />;
  }
}

const Row = ({ label, value }: { label: string; value: string | null | undefined }) => (
  <View style={styles.row}><Text variant="small" style={{ width: 120 }}>{label}</Text><Text style={{ flex: 1 }}>{value || '—'}</Text></View>
);

function ProfileTab({ id }: { id: string }) {
  const q = useProfile(id);
  const p = q.data;
  return (
    <QueryState loading={q.isPending} error={q.error} onRetry={() => void q.refetch()}>
      {p ? (
        <Card style={{ gap: 8 }}>
          <Row label="Name" value={p.fullName} /><Row label="Student ID" value={p.studentId} /><Row label="Admission no." value={p.admissionNumber} />
          <Row label="Class" value={p.className ? `${p.className}${p.sectionName ? ' – ' + p.sectionName : ''}` : null} /><Row label="Roll number" value={p.rollNumber} /><Row label="Academic year" value={p.academicYearName} />
          <Row label="Date of birth" value={formatFull(p.dateOfBirth.slice(0, 10))} /><Row label="Gender" value={p.genderName} /><Row label="Blood group" value={p.bloodGroupName} /><Row label="Address" value={p.address} />
        </Card>
      ) : null}
    </QueryState>
  );
}

function AttendanceTab({ id }: { id: string }) {
  const { from, to } = lastThirtyDays(new Date());
  const q = useAttendance(id, from, to);
  const a = q.data;
  return (
    <QueryState loading={q.isPending} error={q.error} onRetry={() => void q.refetch()} empty={a && a.days.length === 0 ? { title: 'Nothing marked', message: 'No attendance has been marked in the last 30 days.', icon: 'calendar' } : null}>
      {a ? (
        <>
          <Card>
            <Text weight="bold" color={colors.ink}>Last 30 days</Text>
            <Text>{a.summary.byStatus['PRESENT'] ?? 0} present · {a.summary.byStatus['ABSENT'] ?? 0} absent{a.summary.presentPercentage != null ? ` · ${a.summary.presentPercentage}% present` : ''}</Text>
          </Card>
          {a.days.map((d) => (<Card key={d.date} style={styles.line}><Text>{formatFull(d.date)}</Text><Chip label={d.statusName} tone={d.statusCode === 'ABSENT' ? 'danger' : 'success'} /></Card>))}
        </>
      ) : null}
    </QueryState>
  );
}

function HomeworkTab({ id }: { id: string }) {
  const q = useHomework(id);
  const list = q.data ?? [];
  return (
    <QueryState loading={q.isPending} error={q.error} onRetry={() => void q.refetch()} empty={list.length === 0 ? { title: 'No homework', message: 'No homework has been given.', icon: 'book-open' } : null}>
      {list.map((h) => (
        <Card key={h.homeworkId} style={{ gap: 4 }}>
          <Text variant="heading" weight="bold" color={colors.ink}>{h.subjectName}: {h.title}</Text>
          <Text variant="small">Due {formatFull(h.dueDate)} · {h.teacherName}</Text>
          {h.description ? <Text>{h.description}</Text> : null}
        </Card>
      ))}
    </QueryState>
  );
}

function TimetableTab({ id }: { id: string }) {
  const q = useTimetable(id);
  const g = q.data;
  return (
    <QueryState loading={q.isPending} error={q.error} onRetry={() => void q.refetch()} empty={g && g.entryCount === 0 ? { title: 'No timetable', message: 'No timetable has been published for this class yet.', icon: 'clock' } : null}>
      {g ? g.days.map((day) => {
        const lessons = g.rows.map((r) => ({ r, cell: r.cells.find((c) => c.dayOfWeek === day.dayOfWeek)?.entry ?? null })).filter((x) => x.cell);
        if (lessons.length === 0) return null;
        return (
          <Card key={day.dayOfWeek} style={{ gap: 6 }}>
            <Text weight="bold" color={colors.ink}>{day.name}</Text>
            {lessons.map(({ r, cell }) => (<Text key={r.period.periodId}>{r.period.startTime.slice(0, 5)}–{r.period.endTime.slice(0, 5)}  {cell!.subjectName}{cell!.employeeName ? ` · ${cell!.employeeName}` : ''}{cell!.roomNo ? ` · Room ${cell!.roomNo}` : ''}</Text>))}
          </Card>
        );
      }) : null}
    </QueryState>
  );
}

function ExamsTab({ id }: { id: string }) {
  const q = useExams(id);
  const list = q.data ?? [];
  return (
    <QueryState loading={q.isPending} error={q.error} onRetry={() => void q.refetch()} empty={list.length === 0 ? { title: 'No exam dates', message: 'No exam dates have been announced for this class.', icon: 'edit-3' } : null}>
      {list.map((s) => (
        <Card key={s.schemeName} style={{ gap: 6 }}>
          <Text weight="bold" color={colors.ink}>{s.schemeName} · {s.academicYearName}</Text>
          {s.exams.map((e) => (<Text key={e.termName + e.examName}>{e.termName} · {e.examName}: {e.examDate ? formatFull(e.examDate) : 'date to be announced'}</Text>))}
        </Card>
      ))}
    </QueryState>
  );
}

function ReportsTab({ id }: { id: string }) {
  const q = useReportCards(id);
  const [busy, setBusy] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const list = q.data ?? [];
  async function open(publicationId: number) {
    if (busy !== null) return;
    setBusy(publicationId); setError(null);
    try { await openReportCardPdf(id, publicationId); } catch (e) { setError(messageOf(e)); } finally { setBusy(null); }
  }
  return (
    <QueryState loading={q.isPending} error={q.error} onRetry={() => void q.refetch()} empty={list.length === 0 ? { title: 'No report cards', message: 'No report card has been published yet.', icon: 'file-text' } : null}>
      {error ? <ErrorState message={error} /> : null}
      {list.map((c) => (
        <Card key={c.publicationId} style={{ gap: 4 }}>
          <Text weight="bold" color={colors.ink}>{c.schemeName} · {c.scope === 'TERM' ? c.termName ?? 'Term' : 'Whole year'}</Text>
          <Text variant="small">Published {formatFull(c.publishedAt.slice(0, 10))}{c.percent != null ? ` · ${c.percent}%` : ''}{c.grade ? ` · Grade ${c.grade}` : ''}</Text>
          <Pressable onPress={() => void open(c.publicationId)} accessibilityRole="button" accessibilityLabel="Download report card PDF" style={styles.link} disabled={busy !== null}>
            <Icon name="download" size={16} color={colors.primaryText} /><Text color={colors.primaryText} weight="semibold">{busy === c.publicationId ? 'Opening…' : 'Download PDF'}</Text>
          </Pressable>
        </Card>
      ))}
    </QueryState>
  );
}

function FeesTab({ id }: { id: string }) {
  const q = useFees(id);
  const [receiptId, setReceiptId] = useState<number | null>(null);
  const f = q.data;
  return (
    <QueryState loading={q.isPending} error={q.error} onRetry={() => void q.refetch()} empty={f && !f.hasFees ? { title: 'No fees', message: 'No fees have been set for this child yet.', icon: 'credit-card' } : null}>
      {f ? (
        <>
          <Card><Text weight="bold" color={colors.ink}>{f.overallStatus}</Text><Text>Total {rupees(f.totalDue)} · Paid {rupees(f.totalPaid)} · Balance {rupees(f.balance)}</Text></Card>
          <Text weight="bold" color={colors.ink}>Instalments</Text>
          {f.dues.map((d) => (<Card key={d.dueDate + d.feeTypeName} style={styles.line}><View style={{ flex: 1 }}><Text>{d.feeTypeName}</Text><Text variant="small">Due {formatShort(d.dueDate)} · {rupees(d.amountPaid)} of {rupees(d.amountDue)}</Text></View><Chip label={d.status} tone={d.status === 'Paid' ? 'success' : d.status === 'Overdue' ? 'danger' : 'neutral'} /></Card>))}
          <Text weight="bold" color={colors.ink}>Payments and receipts</Text>
          {f.payments.length === 0 ? <Text variant="small">No payment has been made yet.</Text> : null}
          {f.payments.map((p) => (
            <Card key={p.receiptNumber} style={{ gap: 4 }}>
              <Text>{formatShort(p.paymentDate.slice(0, 10))} · {rupees(p.amount)} · {p.paymentMode}</Text>
              {p.lateFee && p.lateFee.decision === 'APPLIED' ? <Text variant="small">Includes late fee {rupees(p.lateFee.amount)}{p.lateFee.ruleText ? ` (${p.lateFee.ruleText})` : ''}</Text> : null}
              {p.paymentId ? (
                <Pressable onPress={() => setReceiptId(receiptId === p.paymentId ? null : p.paymentId!)} accessibilityRole="button" style={styles.link}>
                  <Icon name="file-text" size={16} color={colors.primaryText} /><Text color={colors.primaryText} weight="semibold">Receipt {p.receiptNumber}</Text>
                </Pressable>
              ) : <Text variant="small">Receipt {p.receiptNumber}</Text>}
              {receiptId === p.paymentId && p.paymentId ? <ReceiptView studentId={id} paymentId={p.paymentId} /> : null}
            </Card>
          ))}
        </>
      ) : null}
    </QueryState>
  );
}

function ReceiptView({ studentId, paymentId }: { studentId: string; paymentId: number }) {
  const q = useReceipt(studentId, paymentId);
  if (q.isPending) return <Text variant="small">Loading the receipt…</Text>;
  if (q.error || !q.data) return <ErrorState message={q.error ? messageOf(q.error) : 'The receipt could not be opened.'} onRetry={() => void q.refetch()} />;
  const r = q.data;
  return (
    <View style={styles.receipt} accessibilityLabel={`Receipt ${r.receiptNumber}`}>
      <Text weight="bold" color={colors.ink}>{r.school.name}</Text>
      <Text variant="small">Receipt {r.receiptNumber} · {formatFull(r.paymentDate.slice(0, 10))}</Text>
      <Text variant="small">{r.student.name} ({r.student.studentId}){r.student.className ? ` · ${r.student.className}` : ''}</Text>
      {r.lines.map((l) => (<Text key={l.feeTypeName + l.dueDate}>{l.feeTypeName} (due {formatShort(l.dueDate)}): {rupees(l.allocatedAmount)}</Text>))}
      {r.lateFee && r.lateFee.decision === 'APPLIED' ? <Text>Late fee: {rupees(r.lateFee.amount)}</Text> : null}
      {r.taxAmount ? <Text>Tax: {rupees(r.taxAmount)}</Text> : null}
      <Text weight="bold" color={colors.ink}>Total paid: {rupees(r.amount)} ({r.paymentMode})</Text>
    </View>
  );
}

function LeaveTab({ id }: { id: string }) {
  const q = useLeave(id);
  const list = q.data ?? [];
  return (
    <QueryState loading={q.isPending} error={q.error} onRetry={() => void q.refetch()} empty={list.length === 0 ? { title: 'No leave', message: 'No leave has been requested for this child.', icon: 'calendar' } : null}>
      {list.map((l) => (
        <Card key={l.leaveRequestId} style={{ gap: 4 }}>
          <Text weight="bold" color={colors.ink}>{l.leaveTypeName} · {formatShort(l.fromDate)} to {formatShort(l.toDate)} ({l.days} {l.days === 1 ? 'day' : 'days'})</Text>
          <Chip label={l.status} tone={l.status === 'Approved' ? 'success' : l.status === 'Rejected' ? 'danger' : 'neutral'} />
          {l.reason ? <Text variant="small">{l.reason}</Text> : null}
        </Card>
      ))}
    </QueryState>
  );
}

const styles = StyleSheet.create({
  head: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  banner: { flexDirection: 'row', gap: 8, alignItems: 'flex-start', backgroundColor: colors.warningTint, borderColor: colors.warningTint },
  tabs: { gap: 8, paddingVertical: 2 },
  tab: { minHeight: MIN_TOUCH, paddingHorizontal: 14, borderRadius: 999, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.surface, alignItems: 'center', justifyContent: 'center' },
  tabOn: { backgroundColor: colors.primary, borderColor: colors.primary },
  row: { flexDirection: 'row', gap: 8 },
  line: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  link: { flexDirection: 'row', alignItems: 'center', gap: 6, minHeight: MIN_TOUCH },
  receipt: { gap: 3, padding: 10, borderRadius: 12, backgroundColor: colors.background, borderWidth: 1, borderColor: colors.border },
});
