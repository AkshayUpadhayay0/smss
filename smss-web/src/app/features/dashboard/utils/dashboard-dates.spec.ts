import { shortDay } from './dashboard-dates';

describe('dashboard date helper', () => {
  it('shortDay writes the day and the short month', () => {
    expect(shortDay('2026-10-21')).toBe('21 Oct');
    expect(shortDay('2027-01-05')).toBe('05 Jan');
  });
});
