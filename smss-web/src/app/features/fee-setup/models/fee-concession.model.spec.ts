import { concessionNameWithValue, concessionPaise } from './fee-concession.model';

describe('fee concession model', () => {
  it('the value is shown in brackets only when the name does not already state it', () => {
    expect(concessionNameWithValue('Sibling 10%', 'PERCENTAGE', 10)).toBe('Sibling 10%');
    expect(concessionNameWithValue('Sibling 10 %', 'PERCENTAGE', 10)).toBe('Sibling 10 %');
    expect(concessionNameWithValue('Merit', 'PERCENTAGE', 5)).toBe('Merit (5%)');
    expect(concessionNameWithValue('Merit 5%', 'PERCENTAGE', 12.5)).toBe('Merit 5% (12.5%)');       // a different percentage in the name does not count
    expect(concessionNameWithValue('Staff ward', 'FIXED', 500)).toBe('Staff ward (₹500)');
    expect(concessionNameWithValue('Staff Rs 500', 'FIXED', 500)).toBe('Staff Rs 500');
    expect(concessionNameWithValue('Staff ₹500 off', 'FIXED', 500)).toBe('Staff ₹500 off');
    expect(concessionNameWithValue('Class 5 ward', 'FIXED', 500)).toBe('Class 5 ward (₹500)');       // 5 is not 500
    expect(concessionNameWithValue('Fee waiver', 'FIXED', 2000)).toBe('Fee waiver (₹2,000)');
  });

  it('concessionPaise rounds a percentage half up on exact integer arithmetic (same cases as the server)', () => {
    expect(concessionPaise('PERCENTAGE', 10, 100000)).toBe(10000);    // 10% of 1000.00
    expect(concessionPaise('PERCENTAGE', 10, 33333)).toBe(3333);      // 33.333 -> 33.33
    expect(concessionPaise('PERCENTAGE', 10, 5)).toBe(1);             // 0.005 -> 0.01 (half up)
    expect(concessionPaise('PERCENTAGE', 10, 1)).toBe(0);             // 0.001 -> 0.00
    expect(concessionPaise('PERCENTAGE', 12.5, 10004)).toBe(1251);    // 12.505 -> 12.51
    expect(concessionPaise('PERCENTAGE', 100, 25075)).toBe(25075);
    expect(concessionPaise('PERCENTAGE', 0, 25075)).toBe(0);
  });

  it('a fixed concession is the amount once, never more than the base', () => {
    expect(concessionPaise('FIXED', 100, 100000)).toBe(10000);
    expect(concessionPaise('FIXED', 100, 6000)).toBe(6000);
    expect(concessionPaise('FIXED', 2000, 150000)).toBe(150000);
    expect(concessionPaise('FIXED', 100, 0)).toBe(0);
  });
});
