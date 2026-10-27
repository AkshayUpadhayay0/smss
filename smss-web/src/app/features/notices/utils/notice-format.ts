import { NoticeAudience } from '../models/notice.model';

export const AUDIENCE_LABELS: Record<NoticeAudience, string> = {
  ALL: 'Everyone',
  STAFF: 'Staff',
  STUDENTS_GUARDIANS: 'Students & guardians',
};

export const audienceLabel = (audience: string): string => AUDIENCE_LABELS[audience as NoticeAudience] ?? audience;

/** The API sends UTC instants; read a value without an offset as UTC too, then show it in the viewer's local time. */
export function publishedLabel(iso: string): string {
  const d = new Date(/(Z|[+-]\d\d:?\d\d)$/.test(iso) ? iso : `${iso}Z`);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

/** A one-line preview of a body for lists. */
export const snippet = (body: string, max = 140): string => {
  const flat = body.replace(/\s+/g, ' ').trim();
  return flat.length > max ? `${flat.slice(0, max - 1)}…` : flat;
};
