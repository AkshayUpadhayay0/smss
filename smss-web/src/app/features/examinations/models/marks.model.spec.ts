import { CellEdit, MarksGrid, cellKey, distinct, editsToEntries, enteredCount } from './marks.model';

const grid = (mode: 'MARKS' | 'GRADE' = 'MARKS', skills = false): MarksGrid => ({
  examSubjectId: 1, sectionId: 2, schemeId: 3, schemeName: 'S', termName: 'T', examName: 'E', subjectName: 'Maths', className: 'Class 3', sectionName: 'A', entryMode: mode, maxMarks: mode === 'MARKS' ? 10 : null,
  skills: skills ? [{ skillId: 7, name: 'Reading' }, { skillId: 8, name: 'Writing' }] : [], gradeOptions: [{ code: 'A', label: 'Excellent' }], status: 'DRAFT', version: 2, locked: false,
  canEdit: true, canSubmit: true, canAccept: false, canReopen: false, counts: { students: 2, cells: 2, entered: 1, absent: 0, exempt: 0, missing: 1 },
  students: [
    { studentId: 'S1', admissionNumber: 'A1', rollNumber: '1', fullName: 'Asha', onRoster: true, cells: skills ? [{ skillId: 7, state: 'ENTERED', gradeCode: 'A' }, { skillId: 8 }] : [{ state: 'ENTERED', marks: 8 }] },
    { studentId: 'S2', admissionNumber: 'A2', rollNumber: '2', fullName: 'Ravi', onRoster: true, cells: skills ? [{ skillId: 7 }, { skillId: 8 }] : [{}] },
    { studentId: 'S3', admissionNumber: 'A3', rollNumber: '', fullName: 'Left', onRoster: false, cells: skills ? [{ skillId: 7 }, { skillId: 8 }] : [{ state: 'ENTERED', marks: 5 }] },
  ],
});
const e = (state: CellEdit['state'], marks = '', gradeCode = ''): CellEdit => ({ state, marks, gradeCode });

describe('marks model helpers', () => {
  it('cellKey tells a skill cell from a plain cell', () => {
    expect(cellKey('S1')).toBe('S1|');
    expect(cellKey('S1', 7)).toBe('S1|7');
  });

  it('counts "x of y entered" from the saved values with the unsaved edits laid over them, roster students only', () => {
    expect(enteredCount(grid(), {})).toEqual({ done: 1, total: 2 });                                   // the student who left is not counted
    expect(enteredCount(grid(), { [cellKey('S2')]: e('ENTERED', '6') })).toEqual({ done: 2, total: 2 });
    expect(enteredCount(grid(), { [cellKey('S2')]: e('ENTERED', '  ') })).toEqual({ done: 1, total: 2 });   // blank marks are not an entry
    expect(enteredCount(grid(), { [cellKey('S2')]: e('ABSENT') })).toEqual({ done: 2, total: 2 });
    expect(enteredCount(grid(), { [cellKey('S2')]: e('EXEMPT') })).toEqual({ done: 2, total: 2 });
    expect(enteredCount(grid(), { [cellKey('S1')]: e('ENTERED', '') })).toEqual({ done: 0, total: 2 });      // an emptied cell is no longer entered
  });

  it('counts every skill cell of a skills grid', () => {
    expect(enteredCount(grid('GRADE', true), {})).toEqual({ done: 1, total: 4 });
    expect(enteredCount(grid('GRADE', true), { [cellKey('S2', 7)]: e('ENTERED', '', 'A'), [cellKey('S2', 8)]: e('EXEMPT') })).toEqual({ done: 3, total: 4 });
  });

  it('turns edits into the entries the API takes: marks as numbers, an emptied cell as NONE, absent / exempt without values', () => {
    const entries = editsToEntries(grid(), { [cellKey('S1')]: e('ENTERED', ''), [cellKey('S2')]: e('ENTERED', '7.25') });
    expect(entries).toEqual([{ studentId: 'S1', state: 'NONE' }, { studentId: 'S2', state: 'ENTERED', marks: 7.25 }]);
    expect(editsToEntries(grid(), { [cellKey('S2')]: e('ABSENT') })).toEqual([{ studentId: 'S2', state: 'ABSENT' }]);
  });

  it('sends skill ids and grade codes for a skills grid, and never an edit for a student who left', () => {
    const entries = editsToEntries(grid('GRADE', true), { [cellKey('S2', 8)]: e('ENTERED', '', 'A'), [cellKey('S3', 7)]: e('ENTERED', '', 'A'), [cellKey('S2', 7)]: e('ENTERED', '', '') });
    expect(entries).toEqual([{ studentId: 'S2', skillId: 7, state: 'NONE' }, { studentId: 'S2', skillId: 8, state: 'ENTERED', gradeCode: 'A' }]);
  });

  it('distinct keeps the first of each key, in order', () => {
    expect(distinct([{ a: 1, b: 'x' }, { a: 2, b: 'y' }, { a: 1, b: 'z' }], (r) => r.a)).toEqual([{ a: 1, b: 'x' }, { a: 2, b: 'y' }]);
  });
});
