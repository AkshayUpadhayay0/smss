// Client-side input rules for the teacher forms. They mirror the server's rules so people get instant feedback, but the SERVER is the authority:
// its own message is shown whenever it still refuses something.

export const MAX_MARKS_LIMIT = 9999.99; // the server's numeric(6,2)

export type MarkCheck = { state: 'empty' } | { state: 'invalid'; message: string } | { state: 'ok'; value: number };

/** Parses what a teacher typed ("18", "7.5", "7,5"). Rules: a number from 0 to max with at most two decimals. */
export function checkMark(text: string, max: number): MarkCheck {
  const t = text.trim().replace(',', '.');
  if (t === '') return { state: 'empty' };
  if (!/^\d+(\.\d+)?$/.test(t)) return { state: 'invalid', message: 'Enter a number.' };
  if (!/^\d+(\.\d{1,2})?$/.test(t)) return { state: 'invalid', message: 'At most two decimal places.' };
  const value = Number(t);
  if (value > max) return { state: 'invalid', message: `Must be at most ${max}.` };
  return { state: 'ok', value };
}

/** Maximum marks for a test: a number greater than 0 and at most 9999.99 with at most two decimals. Returns an error message or the value. */
export function checkMaxMarks(text: string): { error: string } | { value: number } {
  const t = text.trim().replace(',', '.');
  if (t === '') return { error: 'Enter the maximum marks.' };
  if (!/^\d+(\.\d{1,2})?$/.test(t)) return { error: 'Enter a number with at most two decimal places.' };
  const value = Number(t);
  if (value <= 0) return { error: 'Maximum marks must be greater than zero.' };
  if (value > MAX_MARKS_LIMIT) return { error: `Maximum marks can be at most ${MAX_MARKS_LIMIT}.` };
  return { value };
}

export const ATTACHMENT_MAX_BYTES = 5 * 1024 * 1024; // 5 MB, like the server
export const ATTACHMENT_EXTENSIONS = ['.pdf', '.png', '.jpg', '.jpeg', '.webp'];

/** null = fine. The server re-checks the real file signature; this only gives an immediate, friendly answer. */
export function validateAttachment(file: { name: string; size?: number | null }): string | null {
  const dot = file.name.lastIndexOf('.');
  const ext = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (!ATTACHMENT_EXTENSIONS.includes(ext)) return 'Attach a PDF or an image (PNG, JPG or WEBP).';
  if (file.size === 0) return 'That file is empty.';
  if (typeof file.size === 'number' && file.size > ATTACHMENT_MAX_BYTES) return 'The file must be 5 MB or smaller.';
  return null;
}

export const HOMEWORK_TITLE_MAX = 200;
export const HOMEWORK_DESCRIPTION_MAX = 2000;
export const LEAVE_REASON_MAX = 500;

export interface Tally {
  present: number;
  absent: number;
  unmarked: number;
}

/** Live counts for the attendance footer. Every student must be explicitly marked before Submit is enabled. */
export function tallyAttendance(studentIds: string[], marks: Record<string, 'PRESENT' | 'ABSENT' | undefined>): Tally {
  let present = 0;
  let absent = 0;
  for (const id of studentIds) {
    if (marks[id] === 'PRESENT') present++;
    else if (marks[id] === 'ABSENT') absent++;
  }
  return { present, absent, unmarked: studentIds.length - present - absent };
}
