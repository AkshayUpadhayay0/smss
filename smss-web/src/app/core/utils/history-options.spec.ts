import { describe, expect, it } from 'vitest';
import { historyClassOptions, historyYearOptions, pickYear } from './history-options';

const years = [
  { academicYearId: 1, yearName: '2025-26', isCurrent: false, statusName: 'Active', isClosed: true },
  { academicYearId: 2, yearName: '2026-27', isCurrent: true, statusName: 'Active', isClosed: false },
  { academicYearId: 3, yearName: '2024-25', isCurrent: false, statusName: 'Inactive', isClosed: false },
];

describe('history selectors', () => {
  it('list every year, closed and inactive ones marked, never dropped', () => {
    expect(historyYearOptions(years).map((o) => o.label)).toEqual(['2025-26 (closed, read-only)', '2026-27 (current)', '2024-25 (inactive)']);
  });

  it('list every class and mark an inactive one', () => {
    const o = historyClassOptions([{ classId: 1, className: 'Nursery', statusName: 'Active' }, { classId: 2, className: 'LKG', statusName: 'Inactive' }]);
    expect(o).toEqual([{ label: 'Nursery', value: '1' }, { label: 'LKG (inactive)', value: '2' }]);
  });

  it('start on the year in the URL, else the current one, else the first', () => {
    expect(pickYear(years, '1')?.academicYearId).toBe(1);
    expect(pickYear(years, 'nope')?.academicYearId).toBe(2);
    expect(pickYear(years.map((y) => ({ ...y, isCurrent: false })))?.academicYearId).toBe(1);
    expect(pickYear([])).toBeUndefined();
  });
});
