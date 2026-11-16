import { PaymentTaxFields, concessionLabel, hasConcession, receiptTotals } from './fee-payment.model';

const base: PaymentTaxFields & { amount: number } = { amount: 1000 };
const taxed: PaymentTaxFields & { amount: number } = { amount: 1180, feeAmount: 1000, grossAmount: 1000, netFeeAmount: 1000, taxName: 'GST 18%', taxRatePercent: 18, taxAmount: 180, totalAmount: 1180 };
const discounted: PaymentTaxFields & { amount: number } = {
  amount: 900, feeAmount: 900, grossAmount: 1000, netFeeAmount: 900, concessionName: 'Sibling 10%', concessionType: 'PERCENTAGE', concessionValue: 10, concessionAmount: 100, totalAmount: 900,
};
const both: PaymentTaxFields & { amount: number } = { ...discounted, amount: 1062, taxName: 'GST 18%', taxRatePercent: 18, taxAmount: 162, totalAmount: 1062 };

describe('receipt totals', () => {
  it('without concession or tax: the single "Total received" line, exactly as before', () => {
    expect(receiptTotals(base)).toEqual([{ label: 'Total received', amount: 1000, total: true }]);
  });

  it('with tax only: Fee amount / Tax / Total paid (no concession lines)', () => {
    expect(receiptTotals(taxed).map((t) => [t.label, t.amount])).toEqual([['Fee amount', 1000], ['Tax: GST 18%', 180], ['Total paid', 1180]]);
  });

  it('with a concession only: Fee amount / Concession (−) / Net fee / Total paid (no tax line)', () => {
    const lines = receiptTotals(discounted);
    expect(lines.map((t) => [t.label, t.amount])).toEqual([['Fee amount', 1000], ['Concession: Sibling 10%', 100], ['Net fee', 900], ['Total paid', 900]]);
    expect(lines[1].deduction).toBe(true);
    expect(lines[3].total).toBe(true);
  });

  it('with both: the order is fee, concession, net fee, tax (on the net), total', () => {
    expect(receiptTotals(both).map((t) => [t.label, t.amount])).toEqual([['Fee amount', 1000], ['Concession: Sibling 10%', 100], ['Net fee', 900], ['Tax: GST 18%', 162], ['Total paid', 1062]]);
  });

  it('a concession name that does not state its value gets it in brackets', () => {
    const p = { ...discounted, concessionName: 'Staff ward', concessionType: 'FIXED' as const, concessionValue: 500, concessionAmount: 500 };
    expect(concessionLabel(p)).toBe('Staff ward (₹500)');
    expect(hasConcession(p)).toBe(true);
    expect(hasConcession(base)).toBe(false);
    expect(hasConcession({ ...discounted, concessionAmount: 0 })).toBe(false);
  });
});
