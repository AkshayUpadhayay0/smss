import { useState } from 'react';
import { Screen } from '../../components/Screen';
import { QueryState } from '../../components/QueryState';
import { MarksEntry } from './MarksEntry';
import { MarksSetup, type TestSetup } from './MarksSetup';
import { useAssessmentTypes, useMyTimetable, useTeacherDashboard } from './queries';

/** Enter test marks: step 1 describes the test, step 2 is the roster (see MarksEntry). */
export function TeacherMarks() {
  const dashboard = useTeacherDashboard(); // the SERVER's date: a test cannot be dated in the future
  const timetable = useMyTimetable();
  const types = useAssessmentTypes();
  const [setup, setSetup] = useState<TestSetup | null>(null);
  const [editing, setEditing] = useState(true);
  // After "Record another" the form starts clean (a new key remounts it).
  const [formKey, setFormKey] = useState(0);

  if (setup && !editing) {
    return (
      <MarksEntry
        setup={setup}
        onEdit={() => setEditing(true)}
        onRecordAnother={() => {
          setSetup(null);
          setEditing(true);
          setFormKey((k) => k + 1);
        }}
      />
    );
  }

  const refresh = () => {
    void dashboard.refetch();
    void timetable.refetch();
    void types.refetch();
  };
  return (
    <Screen title="Marks" subtitle="Record a class test" onRefresh={refresh} refreshing={timetable.isRefetching}>
      <QueryState loading={dashboard.isPending} error={dashboard.error} onRetry={refresh} skeletons={2}>
        {dashboard.data ? (
          <MarksSetup
            key={formKey}
            today={dashboard.data.today}
            initial={setup}
            onContinue={(s) => {
              setSetup(s);
              setEditing(false);
            }}
          />
        ) : null}
      </QueryState>
    </Screen>
  );
}
