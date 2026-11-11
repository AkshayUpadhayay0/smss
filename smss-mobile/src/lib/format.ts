/** Up to two initials from a display name: "Asha Rao" -> "AR". */
export function initials(name: string | null | undefined): string {
  const words = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  const first = words[0][0];
  const last = words.length > 1 ? words[words.length - 1][0] : '';
  return (first + last).toUpperCase();
}

export function firstName(name: string | null | undefined): string {
  return (name ?? '').trim().split(/\s+/)[0] || 'there';
}

/** 18 -> "18", 18.5 -> "18.5", 18.25 -> "18.25". Marks are shown as the API sent them, never rounded or recomputed. */
export function formatMarks(value: number): string {
  return Number.isInteger(value) ? String(value) : String(Number(value.toFixed(2)));
}

/** "Class 5 · A" from whatever parts exist. */
export function classLabel(className: string | null | undefined, sectionName: string | null | undefined): string | null {
  const parts = [className, sectionName].filter((p): p is string => !!p && p.trim().length > 0);
  return parts.length ? parts.join(' · ') : null;
}
