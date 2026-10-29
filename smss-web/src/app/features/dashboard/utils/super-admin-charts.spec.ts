import { CollectionBucket } from '../../subscriptions/models/super-admin-dashboard.model';
import {
  FALLBACK_COLORS,
  collectionSegments,
  compactInr,
  registrationOptions,
  revenueExpenseOptions,
  shortMonth,
  signedInr,
  statusDonutOptions,
  trendOf,
} from './super-admin-charts';

describe('super-admin chart helpers', () => {
  it('compactInr writes rupees the Indian way', () => {
    expect(compactInr(0)).toBe('₹0');
    expect(compactInr(950)).toBe('₹950');
    expect(compactInr(1500)).toBe('₹1.5K');
    expect(compactInr(250000)).toBe('₹2.5L');
    expect(compactInr(30000000)).toBe('₹3Cr');
    expect(compactInr(-1500)).toBe('-₹1.5K');
  });

  it('shortMonth keeps twelve labels short', () => {
    expect(shortMonth('Nov 2025')).toBe("Nov '25");
    expect(shortMonth('x')).toBe('x');
  });

  it('signedInr puts the minus in front of the rupee sign', () => {
    expect(signedInr(-100)).toBe('-₹100.00');
    expect(signedInr(100)).toBe('₹100.00');
  });

  it('trendOf: direction, tone, and "nothing to compare" as null', () => {
    expect(trendOf(null)).toBeNull();
    expect(trendOf(undefined)).toBeNull();
    expect(trendOf(12.5)).toEqual({ text: '12.5%', tone: 'good', up: true });
    expect(trendOf(-3)).toEqual({ text: '3%', tone: 'bad', up: false });
    expect(trendOf(0)).toEqual({ text: '0%', tone: 'flat', up: null });
    // expenses: a rise is bad, a fall is good
    expect(trendOf(10, true)).toEqual({ text: '10%', tone: 'bad', up: true });
    expect(trendOf(-10, true)).toEqual({ text: '10%', tone: 'good', up: false });
  });

  it('collectionSegments: shares of the invoiced amount, and a bucket with invoices is never invisible', () => {
    const buckets: CollectionBucket[] = [
      { key: 'Paid', count: 3, amount: 9990, outstanding: 0 },
      { key: 'Pending', count: 1, amount: 10, outstanding: 10 },
      { key: 'PartiallyPaid', count: 0, amount: 0, outstanding: 0 },
      { key: 'Overdue', count: 0, amount: 0, outstanding: 0 },
    ];
    const s = collectionSegments(buckets);
    expect(s.map((x) => x.label)).toEqual(['Paid', 'Pending', 'Partially paid', 'Overdue']);
    expect(s[0].percent).toBeCloseTo(99.9);
    expect(s[1].width).toBe(2); // 0.1% would be a hairline
    expect(s[2].width).toBe(0);
  });

  it('collectionSegments: nothing invoiced means no segments', () => {
    const s = collectionSegments([{ key: 'Paid', count: 0, amount: 0, outstanding: 0 }]);
    expect(s[0].percent).toBe(0);
    expect(s[0].width).toBe(0);
  });

  it('revenue chart: two series over the months, rupees on the axis and in the tooltip', () => {
    const o = revenueExpenseOptions([
      { month: '2026-09-01', label: 'Sep 2026', revenue: 100000, expenses: 40000 },
      { month: '2026-10-01', label: 'Oct 2026', revenue: 0, expenses: 500 },
    ]);
    expect(o.series).toEqual([{ name: 'Revenue', data: [100000, 0] }, { name: 'Expenses', data: [40000, 500] }]);
    expect((o.xaxis as { categories: string[] }).categories).toEqual(["Sep '26", "Oct '26"]);
    const y = o.yaxis as { labels: { formatter: (v: number) => string } };
    expect(y.labels.formatter(100000)).toBe('₹1L');
    expect(o.tooltip?.y).toMatchObject({});
    expect(o.colors).toEqual([FALLBACK_COLORS.primary, FALLBACK_COLORS.warning]);
  });

  it('registrations chart: one bar per month, whole numbers', () => {
    const o = registrationOptions([{ month: '2026-10-01', label: 'Oct 2026', count: 3 }]);
    expect(o.series).toEqual([{ name: 'Schools registered', data: [3] }]);
    expect((o.yaxis as { labels: { formatter: (v: number) => string } }).labels.formatter(2.4)).toBe('2');
  });

  it('status donut: the four statuses in order, with the total in the middle', () => {
    const o = statusDonutOptions({ trial: 2, active: 5, overdue: 1, cancelled: 0 });
    expect(o.labels).toEqual(['Trial', 'Active', 'Overdue', 'Cancelled']);
    expect(o.series).toEqual([2, 5, 1, 0]);
    const total = (o.plotOptions?.pie?.donut?.labels as { total: { formatter: () => string } }).total;
    expect(total.formatter()).toBe('8');
  });
});
