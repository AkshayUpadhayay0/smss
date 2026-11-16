import { formatRate, taxNameWithRate, taxOptionLabel, taxPaise } from './tax-type.model';

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

  it('the rate is shown in brackets only when the name does not already state it', () => {
    expect(taxNameWithRate('GST 18%', 18)).toBe('GST 18%');
    expect(taxNameWithRate('GST 18 %', 18)).toBe('GST 18 %');
    expect(taxNameWithRate('IGST 12.5%', 12.5)).toBe('IGST 12.5%');
    expect(taxNameWithRate('Service Tax', 5)).toBe('Service Tax (5%)');
    expect(taxNameWithRate('GST', 12.5)).toBe('GST (12.5%)');
    expect(taxNameWithRate('GST 18% (old)', 5)).toBe('GST 18% (old) (5%)');   // a different percentage in the name does not count
    expect(taxNameWithRate('Cess 5.5%', 5)).toBe('Cess 5.5% (5%)');            // 5.5 is not 5
    expect(taxNameWithRate('Zero', 0)).toBe('Zero (0%)');
    expect(taxOptionLabel({ taxName: 'GST 18%', ratePercent: 18 })).toBe('GST 18%');
  });

  it('labels read like "GST (5%)"', () => {
    expect(formatRate(5)).toBe('5%');
    expect(formatRate(12.5)).toBe('12.5%');
    expect(taxOptionLabel({ taxName: 'GST', ratePercent: 18 })).toBe('GST (18%)');
  });
});
