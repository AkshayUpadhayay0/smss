/** "21 Oct" for the attendance chart's axis. (A plain date string, no Date-in-time-zone surprises.) */
export function shortDay(ymd: string): string {
  return new Date(`${ymd}T00:00:00`).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' });
}
