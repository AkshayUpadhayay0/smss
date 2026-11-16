import { formatRate, taxOptionLabel, taxPaise } from './tax-type.model';

describe('tax type model', () => {
  it('taxPaise rounds half up on exact integer arithmetic (same cases as the server)', () => {
    expect(taxPaise(100000, 18)).toBe(18000);   // 1000 x 18% = 180.00
    expect(taxPaise(33333, 5)).toBe(1667);      // 333.33 x 5% = 16.6665 -> 16.67
    expect(taxPaise(1, 5)).toBe(0);             // 0.01 x 5% = 0.0005 -> 0.00
    expect(taxPaise(100000, 0)).toBe(0);        // a rate of 0
    expect(taxPaise(10, 5)).toBe(1);            // 0.10 x 5% = 0.005 exactly half -> 0.01
    expect(taxPaise(30, 5)).toBe(2);            // 0.30 x 5% = 0.015 exactly half -> 0.02 (floating point would say 0.01)
    expect(taxPaise(10000, 12.5)).toBe(1250);
    expect(taxPaise(9999, 100)).toBe(9999);
  });

  it('labels read like "GST (5%)"', () => {
    expect(formatRate(5)).toBe('5%');
    expect(formatRate(12.5)).toBe('12.5%');
    expect(taxOptionLabel({ taxName: 'GST', ratePercent: 18 })).toBe('GST (18%)');
  });
});
