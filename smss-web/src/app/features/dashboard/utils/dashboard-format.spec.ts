import { DONUT_CIRCUMFERENCE, CLASS_STATUS_TONES, dateRange, donutArcs, leaveTone, marksText, timeRange } from './dashboard-format';

describe('dashboard-format', () => {
  it('formats a period as a 12-hour range', () => {
    expect(timeRange('09:00:00', '09:45:00')).toBe('9:00 AM – 9:45 AM');
    expect(timeRange('12:30:00', '13:15:00')).toBe('12:30 PM – 1:15 PM');
  });

  it('colours class statuses: Completed green, In Progress amber, Upcoming gray', () => {
    expect(CLASS_STATUS_TONES).toEqual({ Completed: 'green', InProgress: 'amber', Upcoming: 'gray' });
  });

  it('colours the four leave statuses', () => {
    expect(['Pending', 'Approved', 'Rejected', 'Cancelled'].map(leaveTone)).toEqual(['amber', 'green', 'red', 'gray']);
  });

  it('shows one day or a range of days', () => {
    expect(dateRange('2026-10-05', '2026-10-05')).toBe('05 Oct 2026');
    expect(dateRange('2026-10-05', '2026-10-07')).toBe('05 Oct 2026 – 07 Oct 2026');
  });

  it('shows marks as obtained / max, and Absent when the test was missed', () => {
    expect(marksText(17.5, 20, false)).toBe('17.5 / 20');
    expect(marksText(null, 20, true)).toBe('Absent');
    expect(marksText(undefined, 20, false)).toBe('Absent');
  });

  it('splits the ring into two arcs laid end to end: Present then Absent', () => {
    const { present, absent } = donutArcs(18, 2);
    const c = DONUT_CIRCUMFERENCE;
    expect(present.offset).toBe(0);
    expect(parseFloat(present.dash)).toBeCloseTo(c * 0.9, 5);
    expect(parseFloat(absent.dash)).toBeCloseTo(c * 0.1, 5);
    expect(absent.offset).toBeCloseTo(-c * 0.9, 5);
  });

  it('draws nothing when no day is marked', () => {
    const { present, absent } = donutArcs(0, 0);
    expect(parseFloat(present.dash)).toBe(0);
    expect(parseFloat(absent.dash)).toBe(0);
  });
});
