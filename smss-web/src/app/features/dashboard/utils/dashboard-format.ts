import { formatTime } from '../../calendar/utils/calendar-dates';
import { day } from '../../homework/utils/homework-format';
import { ClassStatus } from '../models/student-dashboard.model';

/** "9:00 AM – 9:45 AM" from two HH:mm:ss times. */
export const timeRange = (start: string, end: string): string => `${formatTime(start)} – ${formatTime(end)}`;

export const CLASS_STATUS_LABELS: Record<ClassStatus, string> = { Completed: 'Completed', InProgress: 'In Progress', Upcoming: 'Upcoming' };

/** Pill colour per class status: Completed green, In Progress amber, Upcoming neutral gray. */
export const CLASS_STATUS_TONES: Record<ClassStatus, 'green' | 'amber' | 'gray'> = { Completed: 'green', InProgress: 'amber', Upcoming: 'gray' };

/** Pill colour per leave status (the same four values the leave screens use). */
export const leaveTone = (status: string): 'amber' | 'green' | 'red' | 'gray' =>
  status === 'Pending' ? 'amber' : status === 'Approved' ? 'green' : status === 'Rejected' ? 'red' : 'gray';

/** "05 Oct 2026" for one day, "05 Oct 2026 – 07 Oct 2026" for a range. */
export const dateRange = (from: string, to: string): string => (from === to ? day(from) : `${day(from)} – ${day(to)}`);

/** Marks as "17.5 / 20", or "Absent" when the student missed the test. */
export const marksText = (obtained: number | null | undefined, max: number, isAbsent: boolean): string => (isAbsent || obtained == null ? 'Absent' : `${obtained} / ${max}`);

export interface DonutArc {
  /** stroke-dasharray "length gap" for a circle of circumference {@link DONUT_CIRCUMFERENCE}. */
  dash: string;
  /** stroke-dashoffset: where the arc starts (arcs are laid end to end, clockwise from the top). */
  offset: number;
}

export const DONUT_RADIUS = 48;
export const DONUT_CIRCUMFERENCE = 2 * Math.PI * DONUT_RADIUS;

/** The Present and Absent arcs of the attendance ring. Two slices only; with nothing marked both are empty (the template draws a plain ring). */
export function donutArcs(present: number, absent: number): { present: DonutArc; absent: DonutArc } {
  const total = present + absent;
  const c = DONUT_CIRCUMFERENCE;
  const presentLength = total === 0 ? 0 : (present / total) * c;
  const absentLength = total === 0 ? 0 : (absent / total) * c;
  return {
    present: { dash: `${presentLength} ${c - presentLength}`, offset: 0 },
    absent: { dash: `${absentLength} ${c - absentLength}`, offset: -presentLength },
  };
}
