import { attendanceTrendOptions, feeCollectionOptions } from './role-dashboard-charts';

describe('role dashboard chart options', () => {
  const points = [
    { date: '2026-10-21', marked: 100, present: 90, presentPercent: 90 },
    { date: '2026-10-22', marked: 0, present: 0, presentPercent: null },
    { date: '2026-10-23', marked: 100, present: 75, presentPercent: 75 },
  ];
  const months = [
    { month: '2026-06-01', label: 'Jun 2026', amount: 0 },
    { month: '2026-07-01', label: 'Jul 2026', amount: 50000 },
    { month: '2026-08-01', label: 'Aug 2026', amount: 70000 },
    { month: '2026-09-01', label: 'Sep 2026', amount: 90000 },
    { month: '2026-10-01', label: 'Oct 2026', amount: 100000 },
    { month: '2026-11-01', label: 'Nov 2026', amount: 125000 },
  ];

  it('attendance: a line of present %, 0-100, with an unmarked day left as a gap (null), not 0', () => {
    const o = attendanceTrendOptions(points);
    expect(o.chart?.type).toBe('area');
    expect((o.series as { data: (number | null)[] }[])[0].data).toEqual([90, null, 75]);
    expect(o.xaxis?.categories).toEqual(['21 Oct', '22 Oct', '23 Oct']);
    expect(o.yaxis).toMatchObject({ min: 0, max: 100 });
  });

  it('attendance tooltip says how many were present, or that nothing was marked', () => {
    const f = (attendanceTrendOptions(points).tooltip?.y as { formatter: (v: number | null, o: { dataPointIndex: number }) => string }).formatter;
    expect(f(90, { dataPointIndex: 0 })).toBe('90% (90 of 100)');
    expect(f(null, { dataPointIndex: 1 })).toBe('Not marked');
  });

  it('fees: one bar per month in rupees with compact axis labels', () => {
    const o = feeCollectionOptions(months);
    expect(o.chart?.type).toBe('bar');
    expect((o.series as { data: number[] }[])[0].data).toEqual([0, 50000, 70000, 90000, 100000, 125000]);
    expect(o.xaxis?.categories).toHaveLength(6);
    const y = (o.yaxis as { labels: { formatter: (v: number) => string } }).labels.formatter;
    expect(y(125000)).toBe('₹1.3L');
  });
});
