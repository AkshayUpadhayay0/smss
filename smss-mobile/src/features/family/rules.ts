import type { ChipState, FamilyChild } from '../../api/family';

// Pure wording and rules for the parent screens (unit-tested). Nothing here touches the network or a native module.

export const CHILD_TABS = [
  { value: 'profile', label: 'Profile' },
  { value: 'attendance', label: 'Attendance' },
  { value: 'homework', label: 'Homework' },
  { value: 'timetable', label: 'Timetable' },
  { value: 'exams', label: 'Exams' },
  { value: 'reports', label: 'Report cards' },
  { value: 'fees', label: 'Fees' },
  { value: 'leave', label: 'Leave' },
] as const;
export type ChildTab = (typeof CHILD_TABS)[number]['value'];

export function chipLabel(state: ChipState): string {
  switch (state) {
    case 'PASSED_OUT': return 'Passed out';
    case 'LEFT': return 'Left the school';
    case 'TRANSFERRED_OUT': return 'Transferred out';
    default: return 'Waiting to be promoted';
  }
}

/** The banner on a read-only child's screens; null for a current child. */
export function readOnlyBanner(state: ChipState | null | undefined): string | null {
  if (!state) return null;
  return state === 'PENDING_PROMOTION'
    ? "This child has not been moved to the new class yet. You are seeing last year's record; it will update once the school promotes them."
    : `${chipLabel(state)}: this is a read-only record of their time at the school. Nothing here can be changed.`;
}

export function classLabel(c: Pick<FamilyChild, 'className' | 'sectionName'>): string {
  return c.className ? (c.sectionName ? `${c.className} – ${c.sectionName}` : c.className) : 'Not in a class';
}

/** The chip on a card: the read-only state, else today's attendance. */
export function cardChip(c: FamilyChild): { label: string; tone: 'neutral' | 'success' | 'danger' | 'warning' } {
  if (c.chipState) return { label: chipLabel(c.chipState), tone: 'warning' };
  if (c.attendanceToday) return { label: `Today: ${c.attendanceToday}`, tone: c.attendanceToday === 'Absent' ? 'danger' : 'success' };
  return { label: 'Today: not marked yet', tone: 'neutral' };
}

export const rupees = (n: number) => `₹${n.toLocaleString('en-IN', { maximumFractionDigits: 2 })}`;

/** The unread badge text: nothing for 0 or when the count could not be read. */
export const unreadBadge = (count: number | undefined | null): string | undefined => (count && count > 0 ? (count > 99 ? '99+' : String(count)) : undefined);

/** Today minus 29 days .. today, as yyyy-MM-dd from a given date (the attendance window; the server never has marks in the future). */
export function lastThirtyDays(now: Date): { from: string; to: string } {
  const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const from = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 29);
  return { from: iso(from), to: iso(now) };
}

/** The month a calendar page shows: its first and last day. */
export function monthWindow(year: number, month: number): { from: string; to: string } {
  const pad = (n: number) => String(n).padStart(2, '0');
  const last = new Date(year, month, 0).getDate();
  return { from: `${year}-${pad(month)}-01`, to: `${year}-${pad(month)}-${pad(last)}` };
}

/** The change-password form's own checks (the server's rule is 8+ characters; it stays the authority). */
export const MIN_PASSWORD = 8;
export function passwordProblem(current: string, next: string, confirm: string): string | null {
  if (!current) return 'Enter your current password.';
  if (next.length < MIN_PASSWORD) return `The new password must be at least ${MIN_PASSWORD} characters.`;
  if (next === current) return 'The new password must be different from the current one.';
  if (next !== confirm) return 'The new passwords do not match.';
  return null;
}
