import { TimetableGrid } from '../../timetable/models/timetable.model';
import { Homework, TeachingCombo } from '../models/homework.model';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const pad = (n: number) => String(n).padStart(2, '0');

/** The browser's local calendar date as yyyy-MM-dd (never through UTC, which could shift the day). */
export const todayYmd = (now = new Date()): string => `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;

/** yyyy-MM-dd -> "05 Oct 2026", read from the text so no timezone can shift the day. */
export const day = (ymd: string): string => `${ymd.slice(8, 10)} ${MONTHS[Number(ymd.slice(5, 7)) - 1]} ${ymd.slice(0, 4)}`;

export type DueState = 'overdue' | 'today' | 'upcoming';

/** Compared with today on the client, in the same spirit as the present-percentage on the attendance screens: no status column exists for it. */
export const dueState = (dueDate: string, today = todayYmd()): DueState => (dueDate < today ? 'overdue' : dueDate === today ? 'today' : 'upcoming');

export const dueLabel = (dueDate: string, today = todayYmd()): string => {
  const state = dueState(dueDate, today);
  if (state === 'today') return 'Due today';
  const days = Math.round((Date.UTC(+dueDate.slice(0, 4), +dueDate.slice(5, 7) - 1, +dueDate.slice(8, 10)) - Date.UTC(+today.slice(0, 4), +today.slice(5, 7) - 1, +today.slice(8, 10))) / 86400000);
  return state === 'overdue' ? `Overdue by ${-days} ${-days === 1 ? 'day' : 'days'}` : `Due in ${days} ${days === 1 ? 'day' : 'days'}`;
};

/** The distinct section + subject pairs of a teacher's timetable (what the Timetable says they teach), in class order. */
export function combosFromGrid(grid: TimetableGrid): TeachingCombo[] {
  const seen = new Map<string, TeachingCombo>();
  for (const row of grid.rows)
    for (const cell of row.cells)
      if (cell.entry) {
        const e = cell.entry;
        const key = `${e.sectionId}:${e.subjectId}`;
        if (!seen.has(key)) seen.set(key, { sectionId: e.sectionId, sectionLabel: `${e.className} – ${e.sectionName}`, subjectId: e.subjectId, subjectName: e.subjectName });
      }
  return [...seen.values()].sort((a, b) => a.sectionLabel.localeCompare(b.sectionLabel, undefined, { numeric: true }) || a.subjectName.localeCompare(b.subjectName));
}

export const sectionLabel = (h: Homework): string => `${h.className} – ${h.sectionName}`;

/** Saves a downloaded blob under a file name (the authenticated endpoint cannot be a plain link: the token travels in a header). */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
}
