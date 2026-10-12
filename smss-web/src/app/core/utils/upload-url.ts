import { environment } from '../../../environments/environment';

/** The API returns uploaded files as relative paths (/uploads/…); make one absolute for <img src>. */
export function toUploadUrl(relativeUrl?: string | null): string | null {
  if (!relativeUrl) return null;
  return /^https?:\/\//i.test(relativeUrl) ? relativeUrl : `${environment.apiUrl}${relativeUrl}`;
}

/** Up to two initials for a placeholder avatar ("Asha K Verma" -> "AV"). */
export function initialsOf(name?: string | null): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  const first = parts[0][0];
  const last = parts.length > 1 ? parts[parts.length - 1][0] : '';
  return (first + last).toUpperCase();
}
