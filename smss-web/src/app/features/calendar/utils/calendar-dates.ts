import { SchoolEvent } from '../models/calendar.model';

/** One cell of the month grid. `date` is yyyy-MM-dd. */
export interface CalendarDay {
  date: string;
  day: number;
  inMonth: boolean;
  isToday: boolean;
}

export const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'] as const;
export const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

const pad = (n: number) => String(n).padStart(2, '0');
/** Local calendar date as yyyy-MM-dd (never through UTC, which would shift the day). */
export const ymd = (d: Date): string => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
export const fromYmd = (s: string): Date => new Date(Number(s.slice(0, 4)), Number(s.slice(5, 7)) - 1, Number(s.slice(8, 10)));

/** A month as 6 weeks x 7 days (Sunday first), so the grid never changes height. Days from the neighbouring months are flagged `inMonth: false`. */
export function monthGrid(year: number, month: number, today: string): CalendarDay[][] {
  const first = new Date(year, month, 1);
  const start = new Date(year, month, 1 - first.getDay());
  const weeks: CalendarDay[][] = [];
  for (let w = 0; w < 6; w++) {
    const week: CalendarDay[] = [];
    for (let d = 0; d < 7; d++) {
      const cell = new Date(start.getFullYear(), start.getMonth(), start.getDate() + w * 7 + d);
      const date = ymd(cell);
      week.push({ date, day: cell.getDate(), inMonth: cell.getMonth() === month, isToday: date === today });
    }
    weeks.push(week);
  }
  return weeks;
}

/** Events happening on a date (a multi-day event is on every day from its start to its end), all-day first, then by start time. */
export function eventsOn(date: string, events: readonly SchoolEvent[]): SchoolEvent[] {
  return events
    .filter((e) => e.startDate <= date && e.endDate >= date)
    .sort((a, b) => (a.startTime ?? '').localeCompare(b.startTime ?? '') || a.eventTitle.localeCompare(b.eventTitle) || a.eventId - b.eventId);
}

/** "9:30 AM" from "09:30:00". */
export function formatTime(t?: string | null): string {
  if (!t) return '';
  const h = Number(t.slice(0, 2));
  return `${h % 12 === 0 ? 12 : h % 12}:${t.slice(3, 5)} ${h < 12 ? 'AM' : 'PM'}`;
}

const dayText = (s: string) => fromYmd(s).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });

/** "22 Oct 2026", "22 – 24 Oct 2026" style range, with times for a timed event. */
export function describeWhen(e: Pick<SchoolEvent, 'startDate' | 'endDate' | 'isAllDay' | 'startTime' | 'endTime'>): string {
  const days = e.startDate === e.endDate ? dayText(e.startDate) : `${dayText(e.startDate)} – ${dayText(e.endDate)}`;
  if (e.isAllDay) return `${days} · All day`;
  return e.startDate === e.endDate ? `${days} · ${formatTime(e.startTime)} – ${formatTime(e.endTime)}` : `${days} · from ${formatTime(e.startTime)} to ${formatTime(e.endTime)}`;
}

/** The label on a calendar chip: the time first for a timed event on the day it starts. */
export function chipLabel(e: SchoolEvent, date: string): string {
  return !e.isAllDay && e.startDate === date ? `${formatTime(e.startTime)} ${e.eventTitle}` : e.eventTitle;
}
