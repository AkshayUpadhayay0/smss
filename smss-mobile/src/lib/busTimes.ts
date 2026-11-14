// Approximate pickup / drop times of a bus on an area ("HH:mm", school local time, as the server sends them). Pure: no React Native imports, unit-tested.
// The wording is fixed by the product owner: "Approximate pickup" and "Approximate drop". These are the time the bus reaches the AREA, never a per-student time.

export interface AreaTimes {
  morningPickupTime?: string | null;
  afternoonDropTime?: string | null;
}

const HHMM = /^([01]\d|2[0-3]):[0-5]\d$/;

/** The server's "HH:mm" or nothing: anything else (a malformed or older answer) is ignored rather than shown. */
export function cleanTime(value: string | null | undefined): string | null {
  const t = typeof value === 'string' ? value.trim() : '';
  return HHMM.test(t) ? t : null;
}

export const pickupText = (value: string | null | undefined): string | null => {
  const t = cleanTime(value);
  return t ? `Approximate pickup ${t}` : null;
};

export const dropText = (value: string | null | undefined): string | null => {
  const t = cleanTime(value);
  return t ? `Approximate drop ${t}` : null;
};

/** "Approximate pickup 07:30 · Approximate drop 14:15", either half alone, or null when there is no time at all. */
export function timesSummary(times: AreaTimes): string | null {
  const parts = [pickupText(times.morningPickupTime), dropText(times.afternoonDropTime)].filter((p): p is string => p !== null);
  return parts.length ? parts.join(' · ') : null;
}

/**
 * The one line on the student's Home card: the earliest pickup time of the student's area ("Pickup about 07:30"), or, when no pickup time is set anywhere, the earliest
 * drop time ("Drop about 14:15"); null when there are none. The phone's clock is deliberately not used to decide what is "next": the server sends no school-local "now" and
 * a wrong guess would mislead. "HH:mm" strings compare correctly because they are zero-padded.
 */
export function homeTimeLine(entries: AreaTimes[]): string | null {
  const earliest = (pick: (e: AreaTimes) => string | null | undefined): string | null => {
    const all = entries.map((e) => cleanTime(pick(e))).filter((t): t is string => t !== null).sort();
    return all.length ? all[0] : null;
  };
  const pickup = earliest((e) => e.morningPickupTime);
  if (pickup) return `Pickup about ${pickup}`;
  const drop = earliest((e) => e.afternoonDropTime);
  return drop ? `Drop about ${drop}` : null;
}
