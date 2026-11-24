import { SelectOption } from '../../shared/components';

/** The little a year / class needs to be offered in a READ-ONLY selector. Both AcademicYear and SchoolClass satisfy it. */
export interface YearLike {
  academicYearId: number;
  yearName: string;
  isCurrent: boolean;
  statusName?: string | null;
  isClosed?: boolean;
}
export interface ClassLike {
  classId: number;
  className: string;
  statusName?: string | null;
}

/**
 * Selectors that only READ (student list, fee reports) must reach every year the school has ever had, so a closed or deactivated year is never hidden: history is still
 * somebody's records. The label says what the year is. Screens that WRITE into a year keep offering only Active, open years.
 */
export function historyYearOptions(years: readonly YearLike[]): SelectOption[] {
  return years.map((y) => ({
    label: y.yearName + (y.isCurrent ? ' (current)' : '') + (y.isClosed ? ' (closed, read-only)' : '') + (y.statusName === 'Inactive' ? ' (inactive)' : ''),
    value: String(y.academicYearId),
  }));
}

/** Every class, an inactive one marked: a class that was switched off later still has students in past years. */
export function historyClassOptions(classes: readonly ClassLike[]): SelectOption[] {
  return classes.map((c) => ({ label: c.className + (c.statusName === 'Active' ? '' : ' (inactive)'), value: String(c.classId) }));
}

/** The year to start on: the one asked for in the URL, else the current one, else the first. */
export function pickYear<T extends YearLike>(years: readonly T[], wantedId?: string | null): T | undefined {
  return years.find((y) => String(y.academicYearId) === wantedId) ?? years.find((y) => y.isCurrent) ?? years[0];
}
