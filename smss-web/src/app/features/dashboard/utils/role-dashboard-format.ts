/**
 * Pieces shared by the Principal, Accountant and Admission Staff dashboards: a headline card, an alert row and the "Unavailable" rule (a part the server could not load is
 * said to be unavailable, never shown as 0).
 */
export interface DashStatCard {
  id: string;
  label: string;
  value: string;
  /** The line under the value. */
  note: string;
  /** Where the card goes; null when the role has no screen for it. */
  link: string | null;
  tone: 'primary' | 'success' | 'info' | 'warning' | 'danger';
  unavailable: boolean;
}

export interface DashAlert {
  id: string;
  title: string;
  detail: string;
  link: string | null;
  /** Nothing needs attention. */
  clear: boolean;
  unavailable: boolean;
}

export const UNAVAILABLE_NOTE = 'Could not be loaded';

export const plural = (n: number, one: string, many = one + 's'): string => `${n} ${n === 1 ? one : many}`;

/** A card whose number could not be loaded says so instead of showing 0. */
export function statCard(c: Omit<DashStatCard, 'unavailable'>, isDown: boolean): DashStatCard {
  return isDown ? { ...c, value: 'Unavailable', note: UNAVAILABLE_NOTE, unavailable: true } : { ...c, unavailable: false };
}

/** An alert row: a count of 0 is a "clear" row, a missing count is "unavailable". */
export function alertRow(id: string, title: string, count: number | null, isDown: boolean, text: (n: number) => string, none: string, link: string | null): DashAlert {
  if (isDown || count == null) return { id, title, detail: UNAVAILABLE_NOTE, link, clear: false, unavailable: true };
  return { id, title, detail: count > 0 ? text(count) : none, link, clear: count === 0, unavailable: false };
}
