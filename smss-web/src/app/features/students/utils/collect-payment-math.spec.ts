import { TaxType } from '../../fee-setup/models/tax-type.model';
import { ConcessionChoice } from '../models/fee-payment.model';
import { PayRow, allocateOldestFirst, buildPreview, checkReceived, concessionApplies, rowError } from './collect-payment-math';

const row = (dueId: number, feeTypeId: number, outstanding: number, checked = true, amount = (outstanding / 100).toFixed(2)): PayRow => ({ dueId, feeTypeId, feeTypeName: 'Fee', outstanding, checked, amount });
const con = (type: 'PERCENTAGE' | 'FIXED', value: number, feeTypeId: number | null = null): ConcessionChoice =>
  ({ concessionId: 1, concessionName: 'C', concessionCode: 'C', concessionType: type, value, applicableFeeTypeId: feeTypeId });
const gst = (rate: number): TaxType => ({ taxTypeId: 1, taxName: 'GST', taxCode: 'GST', ratePercent: rate, isActive: true, createdAt: '', updatedAt: '' });

describe('collect payment math', () => {
  it('rowError: a ticked line is 0.01 at least, the outstanding at most, with at most 2 decimals; an unticked line never errors', () => {
    expect(rowError(row(1, 1, 5000))).toBeNull();
    expect(rowError(row(1, 1, 5000, true, '0.01'))).toBeNull();
    expect(rowError(row(1, 1, 5000, true, '50.00'))).toBeNull();
    for (const bad of ['', '0', '0.00', '-1', '50.01', '1.234', 'abc']) expect(rowError(row(1, 1, 5000, true, bad)), bad).not.toBeNull();
    expect(rowError(row(1, 1, 5000, false, 'abc'))).toBeNull();
  });

  it('an empty selection previews as all zeros, with or without a concession and tax', () => {
    const none = buildPreview([row(1, 1, 5000, false)], con('FIXED', 700), gst(18));
    expect(none).toEqual({ grossPaise: 0, baseConcessionPaise: 0, concessionPaise: 0, netPaise: 0, taxPaise: 0, totalPaise: 0, waived: false });
  });

  it('the worked example in paise: 1000 less 10% is 900, GST 18% is 162, total 1062; rounding is half up', () => {
    expect(buildPreview([row(1, 1, 100000)], con('PERCENTAGE', 10), gst(18))).toMatchObject({ grossPaise: 100000, concessionPaise: 10000, netPaise: 90000, taxPaise: 16200, totalPaise: 106200 });
    expect(buildPreview([row(1, 1, 33333)], con('PERCENTAGE', 10), gst(18))).toMatchObject({ concessionPaise: 3333, netPaise: 30000, taxPaise: 5400, totalPaise: 35400 });
    expect(buildPreview([row(1, 1, 5)], con('PERCENTAGE', 10), null)).toMatchObject({ concessionPaise: 1, netPaise: 4 });   // 0.005 -> 0.01
  });

  it('the discount is on the matching lines only and never above its base; never a negative net or total', () => {
    const rows = [row(1, 1, 50000), row(2, 2, 20000)];
    expect(buildPreview(rows, con('PERCENTAGE', 50, 2), null)).toMatchObject({ baseConcessionPaise: 20000, concessionPaise: 10000, netPaise: 60000 });
    expect(buildPreview(rows, con('FIXED', 999999, 2), null)).toMatchObject({ concessionPaise: 20000, netPaise: 50000 });
    const all = buildPreview(rows, con('PERCENTAGE', 100), gst(18));
    expect(all).toMatchObject({ netPaise: 0, taxPaise: 0, totalPaise: 0, waived: true });
    expect(buildPreview([row(1, 1, 50000)], con('PERCENTAGE', 10, 2), null)).toMatchObject({ concessionPaise: 0, netPaise: 50000, waived: false });   // no matching line: no discount
  });

  it('lines with an error are left out of every figure', () => {
    expect(buildPreview([row(1, 1, 5000, true, '999'), row(2, 1, 3000)], null, null)).toMatchObject({ grossPaise: 3000, totalPaise: 3000 });
  });

  it('concessionApplies looks at the SELECTED, valid lines only', () => {
    expect(concessionApplies(con('FIXED', 10), [row(1, 1, 100, false)])).toBe(false);
    expect(concessionApplies(con('FIXED', 10), [row(1, 1, 100)])).toBe(true);
    expect(concessionApplies(con('FIXED', 10, 2), [row(1, 1, 100)])).toBe(false);
    expect(concessionApplies(con('FIXED', 10, 2), [row(1, 1, 100), row(2, 2, 100)])).toBe(true);
    expect(concessionApplies(con('FIXED', 10, 2), [row(2, 2, 100, true, '5000')])).toBe(false);   // its only matching line is invalid
  });

  it('checkReceived: Cash needs at least the total and reports the change; other modes need the exact total; a waiver needs nothing', () => {
    expect(checkReceived('106.20', 10620, 'Cash', false)).toEqual({ ok: true, error: null, changePaise: 0 });
    expect(checkReceived('200', 10620, 'Cash', false)).toEqual({ ok: true, error: null, changePaise: 9380 });
    expect(checkReceived('100', 10620, 'Cash', false).ok).toBe(false);
    expect(checkReceived('106.20', 10620, 'UPI', false).ok).toBe(true);
    expect(checkReceived('200', 10620, 'UPI', false).ok).toBe(false);
    expect(checkReceived('106.19', 10620, 'Card', false).ok).toBe(false);
    expect(checkReceived('', 10620, 'Cash', false).ok).toBe(false);
    expect(checkReceived('1.234', 10620, 'Cash', false).ok).toBe(false);
    expect(checkReceived('', 0, 'Cash', true).ok).toBe(true);
  });

  it('allocateOldestFirst fills oldest lines first and reports what did not fit', () => {
    const out = allocateOldestFirst([row(1, 1, 1500, false), row(2, 1, 2500, false), row(3, 1, 800, false)], 3000);
    expect(out.rows.map((r) => [r.checked, r.amount])).toEqual([[true, '15.00'], [true, '15.00'], [false, '8.00']]);
    expect(out.leftPaise).toBe(0);
    expect(allocateOldestFirst([row(1, 1, 1000, false)], 5000).leftPaise).toBe(4000);
  });
});
