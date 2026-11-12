import { useState } from 'react';
import { QueryState } from '../../components/QueryState';
import { Screen } from '../../components/Screen';
import { Segmented } from '../../components/Segmented';
import { MyHomeworkList } from './MyHomework';
import { PublishHomework } from './PublishHomework';
import { useMyHomework, useMyTimetable, useTeacherDashboard } from './queries';

type Tab = 'publish' | 'mine';

export function TeacherHomework() {
  const [tab, setTab] = useState<Tab>('publish');
  const dashboard = useTeacherDashboard(); // the SERVER's date for due-date limits and the Due chips
  const timetable = useMyTimetable();
  const mine = useMyHomework();
  const refresh = () => {
    void dashboard.refetch();
    void timetable.refetch();
    void mine.refetch();
  };
  return (
    <Screen title="Homework" onRefresh={refresh} refreshing={mine.isRefetching || timetable.isRefetching}>
      <Segmented
        value={tab}
        onChange={setTab}
        options={[
          { value: 'publish', label: 'Publish new' },
          { value: 'mine', label: 'My homework' },
        ]}
      />
      <QueryState loading={dashboard.isPending} error={dashboard.error} onRetry={refresh} skeletons={2}>
        {dashboard.data ? tab === 'publish' ? <PublishHomework today={dashboard.data.today} onShowMine={() => setTab('mine')} /> : <MyHomeworkList today={dashboard.data.today} /> : null}
      </QueryState>
    </Screen>
  );
}
