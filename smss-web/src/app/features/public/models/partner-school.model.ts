/** One card of GET /api/public/partner-schools. The API returns nothing else about a school. */
export interface PartnerSchool {
  name: string;
  logoUrl: string | null;
  city: string | null;
  state: string | null;
}

/** More schools than this scroll as a marquee; fewer sit in a static centred grid. */
export const MARQUEE_MIN_SCHOOLS = 8;

/** Up to two initials from the school's name ("Green Hills School" -> "GH"), skipping small joining words. */
export function schoolInitials(name: string): string {
  const skip = new Set(['the', 'of', 'and', 'for', 'a', 'an']);
  const words = name.split(/\s+/).filter(Boolean);
  const significant = words.filter((w) => !skip.has(w.toLowerCase()));
  const pick = (significant.length ? significant : words).slice(0, 2);
  return pick.map((w) => Array.from(w)[0]?.toUpperCase() ?? '').join('') || '?';
}

/** A stable hue (0-359) per name, so a school keeps its colour. */
export function schoolHue(name: string): number {
  let h = 0;
  for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) % 360;
  return h;
}
