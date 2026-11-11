// Date helpers on plain "yyyy-MM-dd" strings. Everything is computed in UTC arithmetic from the strings the API sends, never from the phone's
// clock or time zone: "today" always comes from the server (dashboard / attendance responses).

export type Ymd = string;

const DAY_MS = 86_400_000;
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const MONTHS_LONG = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
export const WEEKDAYS_SHORT = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
export const WEEKDAYS_LONG = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

const pad = (n: number) => String(n).padStart(2, '0');

export function ymd(year: number, month: number, day: number): Ymd {
  return `${year}-${pad(month)}-${pad(day)}`;
}

function toUtc(value: Ymd): number {
  const [y, m, d] = value.slice(0, 10).split('-').map(Number);
  return Date.UTC(y, m - 1, d);
}

export function parts(value: Ymd): { year: number; month: number; day: number } {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  return { year, month, day };
}

export function addDays(value: Ymd, n: number): Ymd {
  const d = new Date(toUtc(value) + n * DAY_MS);
  return ymd(d.getUTCFullYear(), d.getUTCMonth() + 1, d.getUTCDate());
}

/** a - b in whole days. */
export function diffDays(a: Ymd, b: Ymd): number {
  return Math.round((toUtc(a) - toUtc(b)) / DAY_MS);
}

/** Monday = 0 ... Sunday = 6. */
export function weekdayMonFirst(value: Ymd): number {
  return (new Date(toUtc(value)).getUTCDay() + 6) % 7;
}

export function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/** The API's day-of-week numbering: 1 = Monday ... 6 = Saturday (Sunday is never valid). */
export function apiDayOfWeek(value: Ymd): number {
  return weekdayMonFirst(value) + 1;
}

/** "5 Nov" */
export function formatShort(value: Ymd): string {
  const { month, day } = parts(value);
  return `${day} ${MONTHS[month - 1]}`;
}

/** "5 Nov 2026" */
export function formatFull(value: Ymd): string {
  const { year } = parts(value);
  return `${formatShort(value)} ${year}`;
}

/** "November 2026" */
export function formatMonthYear(year: number, month: number): string {
  return `${MONTHS_LONG[month - 1]} ${year}`;
}

/** "12 November" for accessibility labels. */
export function formatDayLong(value: Ymd): string {
  const { month, day } = parts(value);
  return `${day} ${MONTHS_LONG[month - 1]}`;
}

/** "08:30:00" -> "8:30 AM" */
export function formatTime(hms: string): string {
  const [h, m] = hms.split(':').map(Number);
  const suffix = h >= 12 ? 'PM' : 'AM';
  return `${h % 12 === 0 ? 12 : h % 12}:${pad(m)} ${suffix}`;
}

export function monthKey(year: number, month: number): number {
  return year * 12 + (month - 1);
}

export function shiftMonth(year: number, month: number, by: number): { year: number; month: number } {
  const k = monthKey(year, month) + by;
  return { year: Math.floor(k / 12), month: (k % 12) + 1 };
}

/** First and last day of a month. */
export function monthRange(year: number, month: number): { from: Ymd; to: Ymd } {
  return { from: ymd(year, month, 1), to: ymd(year, month, daysInMonth(year, month)) };
}
