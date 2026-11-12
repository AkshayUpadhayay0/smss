import { useState } from 'react';
import { Card } from '../../components/Card';
import { DateField } from '../../components/DateField';
import { Input } from '../../components/Input';
import { QueryState } from '../../components/QueryState';
import { Select } from '../../components/Select';
import { Button } from '../../components/Button';
import { teachingOptions } from './derive';
import { useAssessmentTypes, useMyTimetable } from './queries';
import { checkMaxMarks, HOMEWORK_TITLE_MAX } from './rules';

export interface TestSetup {
  academicYearId: number;
  sectionId: number;
  sectionLabel: string;
  subjectId: number;
  subjectName: string;
  typeId: number;
  typeName: string;
  title: string;
  testDate: string;
  maxMarks: number;
  maxText: string;
}

interface Errors {
  section?: string;
  subject?: string;
  type?: string;
  title?: string;
  max?: string;
}

/** Step 1 of entering marks: which test is it? Sections and subjects come from the teacher's own timetable; the type list from GET /Assessment/types. */
export function MarksSetup({ today, initial, onContinue }: { today: string; initial: TestSetup | null; onContinue: (setup: TestSetup) => void }) {
  const timetable = useMyTimetable();
  const types = useAssessmentTypes();
  const options = teachingOptions(timetable.data);

  const [sectionId, setSectionId] = useState<number | null>(initial?.sectionId ?? null);
  const [subjectId, setSubjectId] = useState<number | null>(initial?.subjectId ?? null);
  const [typeId, setTypeId] = useState<number | null>(initial?.typeId ?? null);
  const [title, setTitle] = useState(initial?.title ?? '');
  const [date, setDate] = useState<string>(initial?.testDate ?? today);
  const [maxText, setMaxText] = useState(initial?.maxText ?? '');
  const [errors, setErrors] = useState<Errors>({});

  const section = options.find((o) => o.sectionId === sectionId);
  const refresh = () => {
    void timetable.refetch();
    void types.refetch();
  };

  function next() {
    const e: Errors = {};
    if (sectionId === null || !section) e.section = 'Choose a class and section.';
    const subject = section?.subjects.find((s) => s.subjectId === subjectId);
    if (!subject) e.subject = 'Choose a subject.';
    const type = types.data?.find((t) => t.assessmentTypeId === typeId);
    if (!type) e.type = 'Choose a test type.';
    if (!title.trim()) e.title = 'Enter a title.';
    const max = checkMaxMarks(maxText);
    if ('error' in max) e.max = max.error;
    setErrors(e);
    if (Object.keys(e).length > 0 || !section || !subject || !type || 'error' in max || !timetable.data) return;
    onContinue({
      academicYearId: timetable.data.academicYearId,
      sectionId: section.sectionId,
      sectionLabel: section.label,
      subjectId: subject.subjectId,
      subjectName: subject.subjectName,
      typeId: type.assessmentTypeId,
      typeName: type.typeName,
      title: title.trim(),
      testDate: date,
      maxMarks: max.value,
      maxText,
    });
  }

  return (
    <QueryState loading={timetable.isPending || types.isPending} error={timetable.error ?? types.error} onRetry={refresh} skeletons={2}>
      <Card style={{ gap: 16 }}>
        <Select
          label="Class and section"
          value={sectionId}
          options={options.map((o) => ({ value: o.sectionId, label: o.label }))}
          placeholder="Choose class and section"
          emptyText="You have no classes in the timetable yet."
          error={errors.section}
          onChange={(v) => {
            setSectionId(v);
            setSubjectId(null);
          }}
        />
        <Select
          label="Subject"
          value={subjectId}
          options={(section?.subjects ?? []).map((s) => ({ value: s.subjectId, label: s.subjectName }))}
          placeholder={section ? 'Choose subject' : 'Choose a section first'}
          disabled={!section}
          error={errors.subject}
          onChange={setSubjectId}
        />
        <Select
          label="Test type"
          value={typeId}
          options={(types.data ?? []).map((t) => ({ value: t.assessmentTypeId, label: t.typeName }))}
          placeholder="Choose test type"
          error={errors.type}
          onChange={setTypeId}
        />
        <Input label="Title" value={title} onChangeText={setTitle} maxLength={HOMEWORK_TITLE_MAX} error={errors.title} />
        <DateField label="Test date" value={date} onChange={setDate} max={today} initial={today} hint="A test cannot be recorded for a future date." />
        <Input label="Maximum marks" value={maxText} onChangeText={setMaxText} keyboardType="decimal-pad" error={errors.max} />
        <Button title="Continue" icon="arrow-right" onPress={next} disabled={options.length === 0} />
      </Card>
    </QueryState>
  );
}
