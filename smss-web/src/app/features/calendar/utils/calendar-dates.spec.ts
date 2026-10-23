import { SchoolEvent } from '../models/calendar.model';
import { chipLabel, describeWhen, eventsOn, formatTime, fromYmd, monthGrid, ymd } from './calendar-dates';

const ev = (id: number, title: string, start: string, end: string, extra: Partial<SchoolEvent> = {}): SchoolEvent => ({
  eventId: id, academicYearId: 1, eventTitle: title, startDate: start, endDate: end, isAllDay: true, startTime: null, endTime: null,
  eventCategoryId: 1, categoryName: 'Holiday', categoryCode: 'HOLIDAY', colorHex: '#22c55e', ...extra,
});

describe('calendar dates', () => {
  it('ymd / fromYmd round-trip a local date with no timezone drift', () => {
    expect(ymd(fromYmd('2026-03-01'))).toBe('2026-03-01');
    expect(ymd(fromYmd('2026-12-31'))).toBe('2026-12-31');
  });

  it('monthGrid is always 6 weeks x 7 days, Sunday first, with the neighbouring months flagged', () => {
    const weeks = monthGrid(2026, 9, '2026-10-22');                  // October 2026 starts on a Thursday
    expect(weeks).toHaveLength(6);
    expect(weeks.every((w) => w.length === 7)).toBe(true);
    expect(weeks[0][0].date).toBe('2026-09-27');                      // the Sunday before
    expect(weeks[0][4]).toMatchObject({ date: '2026-10-01', inMonth: true });
    expect(weeks[0][3]).toMatchObject({ date: '2026-09-30', inMonth: false });
    expect(weeks[5][6].date).toBe('2026-11-07');
    const days = weeks.flat();
    expect(new Set(days.map((d) => d.date)).size).toBe(42);           // no day twice
    expect(days.filter((d) => d.isToday).map((d) => d.date)).toEqual(['2026-10-22']);
    expect(days.filter((d) => d.inMonth)).toHaveLength(31);
  });

  it('a month that starts on a Sunday begins its grid that very day (February 2026)', () => {
    const weeks = monthGrid(2026, 1, '2026-02-10');
    expect(weeks[0][0]).toMatchObject({ date: '2026-02-01', inMonth: true });
    expect(weeks.flat().filter((d) => d.inMonth)).toHaveLength(28);
  });

  it('works across a year boundary', () => {
    const weeks = monthGrid(2026, 11, '2026-12-01');                  // December 2026
    expect(weeks[5][6].date.startsWith('2027-01')).toBe(true);
  });

  it('eventsOn includes a multi-day event on EVERY day it covers (first, middle, last) and nothing outside', () => {
    const list = [ev(1, 'Winter break', '2026-12-20', '2026-12-24'), ev(2, 'One day', '2026-12-22', '2026-12-22')];
    expect(eventsOn('2026-12-19', list)).toEqual([]);
    expect(eventsOn('2026-12-20', list).map((e) => e.eventId)).toEqual([1]);
    expect(eventsOn('2026-12-22', list).map((e) => e.eventId).sort()).toEqual([1, 2]);
    expect(eventsOn('2026-12-24', list).map((e) => e.eventId)).toEqual([1]);
    expect(eventsOn('2026-12-25', list)).toEqual([]);
  });

  it('eventsOn puts all-day events first, then timed ones by start time', () => {
    const list = [
      ev(1, 'Late', '2026-10-22', '2026-10-22', { isAllDay: false, startTime: '15:00:00', endTime: '16:00:00' }),
      ev(2, 'Early', '2026-10-22', '2026-10-22', { isAllDay: false, startTime: '08:00:00', endTime: '09:00:00' }),
      ev(3, 'All day', '2026-10-22', '2026-10-22'),
    ];
    expect(eventsOn('2026-10-22', list).map((e) => e.eventTitle)).toEqual(['All day', 'Early', 'Late']);
  });

  it('formatTime gives a 12-hour clock, including midnight and noon', () => {
    expect([formatTime('00:05:00'), formatTime('09:30:00'), formatTime('12:00:00'), formatTime('13:45:00'), formatTime('23:59:00'), formatTime(null)]).toEqual(['12:05 AM', '9:30 AM', '12:00 PM', '1:45 PM', '11:59 PM', '']);
  });

  it('describeWhen words a single day, a range, and times', () => {
    expect(describeWhen(ev(1, 'a', '2026-10-22', '2026-10-22'))).toBe('22 Oct 2026 · All day');
    expect(describeWhen(ev(1, 'a', '2026-10-22', '2026-10-24'))).toBe('22 Oct 2026 – 24 Oct 2026 · All day');
    expect(describeWhen(ev(1, 'a', '2026-10-22', '2026-10-22', { isAllDay: false, startTime: '09:30:00', endTime: '11:00:00' }))).toBe('22 Oct 2026 · 9:30 AM – 11:00 AM');
    expect(describeWhen(ev(1, 'a', '2026-10-22', '2026-10-23', { isAllDay: false, startTime: '18:00:00', endTime: '09:00:00' }))).toContain('from 6:00 PM to 9:00 AM');
  });

  it('chipLabel shows the time only on the day a timed event starts', () => {
    const timed = ev(1, 'Camp', '2026-10-22', '2026-10-23', { isAllDay: false, startTime: '18:00:00', endTime: '09:00:00' });
    expect(chipLabel(timed, '2026-10-22')).toBe('6:00 PM Camp');
    expect(chipLabel(timed, '2026-10-23')).toBe('Camp');
    expect(chipLabel(ev(2, 'Holiday', '2026-10-22', '2026-10-22'), '2026-10-22')).toBe('Holiday');
  });
});
