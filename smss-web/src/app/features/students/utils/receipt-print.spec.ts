import { FeeReceipt } from '../models/fee-payment.model';
import { receiptHtml } from './receipt-print';

const receipt: FeeReceipt = {
  paymentId: 1, receiptNumber: 'RCP-2026-0007', paymentDate: '2026-10-16T08:30:00Z', amount: 3500, paymentMode: 'Cash', remarks: 'cheque <b>no. 5</b>', collectedByName: 'Office', status: 'Active',
  school: { name: 'Delhi <Public> School', address: 'Main Road, 110001', phone: '9876543210', email: 'office@dps.test' },
  student: { studentId: 'DPS001ST001', name: 'Abhinav "AK" Kumar', className: 'Class 1', academicYearName: '2026-27' },
  lines: [
    { feeDueId: 1, feeTypeName: 'Tuition', frequencyName: 'Monthly', dueDate: '2026-04-10', amountDue: 2500, allocatedAmount: 2500, paidToDate: 2500 },
    { feeDueId: 2, feeTypeName: 'Library & Lab', frequencyName: 'Annual', dueDate: '2026-04-10', amountDue: 1000, allocatedAmount: 1000, paidToDate: 1000 },
  ],
};

describe('receiptHtml', () => {
  const html = receiptHtml(receipt);

  describe('tax lines and the school GSTIN', () => {
    const taxed: FeeReceipt = { ...receipt, feeAmount: 3500, taxName: 'Service Tax', taxCode: 'SVC', taxRatePercent: 5, taxAmount: 175, totalAmount: 3675, amount: 3675, school: { ...receipt.school, gstin: '09ABCDE1234F1Z5' } };

    it('a tax receipt prints the GSTIN (escaped) and the tax line with its rate in brackets', () => {
      const h = receiptHtml(taxed);
      expect(h).toContain('GSTIN: 09ABCDE1234F1Z5');
      expect(h).toContain('Tax: Service Tax (5%)');
      expect(receiptHtml({ ...taxed, school: { ...taxed.school, gstin: '<b>X</b>' } })).toContain('GSTIN: &lt;b&gt;X&lt;/b&gt;');
    });

    it('a name that already states the rate is not repeated: "Tax: GST 18%"', () => {
      const h = receiptHtml({ ...taxed, taxName: 'GST 18%', taxRatePercent: 18 });
      expect(h).toContain('Tax: GST 18%<');
      expect(h).not.toContain('GST 18% (18%)');
    });

    it('no GSTIN without tax, even if the school has one; none when it is empty', () => {
      expect(receiptHtml({ ...receipt, school: { ...receipt.school, gstin: '09ABCDE1234F1Z5' } })).not.toContain('GSTIN');
      expect(receiptHtml({ ...taxed, school: { ...taxed.school, gstin: '' } })).not.toContain('GSTIN');
      expect(receiptHtml({ ...taxed, school: { ...taxed.school, gstin: '   ' } })).not.toContain('GSTIN');
      expect(receiptHtml({ ...taxed, school: { ...taxed.school, gstin: null } })).not.toContain('GSTIN');
    });
  });

  it('an active receipt has no VOID marking', () => {
    expect(html).not.toContain('watermark">VOID');
    expect(html).not.toContain('void-banner"><strong>');
  });

  it('a voided receipt is marked VOID with who / when / why (escaped), and still lists everything', () => {
    const v = receiptHtml({ ...receipt, status: 'Voided', voidedAt: '2026-10-17T09:00:00Z', voidedByName: 'Acc <b>ountant</b>', voidReason: 'Wrong <i>student</i>' });
    expect(v).toContain('<div class="watermark">VOID</div>');
    expect(v).toContain('<strong>VOID</strong>');
    expect(v).toContain('Acc &lt;b&gt;ountant&lt;/b&gt;');
    expect(v).toContain('Wrong &lt;i&gt;student&lt;/i&gt;');
    expect(v).not.toContain('<i>student</i>');
    expect(v).toContain('(VOID)');
    expect(v).toContain('RCP-2026-0007');
    expect(v).toContain('Tuition');
  });

  it('carries the receipt number, student, installments and the total', () => {
    expect(html).toContain('RCP-2026-0007');
    expect(html).toContain('DPS001ST001');
    expect(html).toContain('Tuition');
    expect(html).toContain('₹3,500.00');
    expect(html).toContain('Total received');
  });

  it('escapes anything typed by people (names, remarks) so it cannot inject markup into the printed page', () => {
    expect(html).not.toContain('<Public>');
    expect(html).toContain('Delhi &lt;Public&gt; School');
    expect(html).toContain('Abhinav &quot;AK&quot; Kumar');
    expect(html).toContain('cheque &lt;b&gt;no. 5&lt;/b&gt;');
    expect(html).toContain('Library &amp; Lab');
  });

  describe('concession lines', () => {
    const discounted: FeeReceipt = {
      ...receipt, amount: 1062, feeAmount: 900, grossAmount: 1000, netFeeAmount: 900, concessionName: 'Sibling 10%', concessionCode: 'SIB10', concessionType: 'PERCENTAGE', concessionValue: 10,
      concessionAmount: 100, concessionReason: 'second child <b>in</b> school', concessionAppliedByName: 'Priya',
      taxName: 'GST 18%', taxCode: 'GST18', taxRatePercent: 18, taxAmount: 162, totalAmount: 1062,
    };

    it('prints fee amount, concession (minus), net fee, tax and total paid, in that order', () => {
      const h = receiptHtml(discounted);
      const at = (s: string) => h.indexOf(s);
      expect(at('Fee amount')).toBeGreaterThan(0);
      expect(at('Concession: Sibling 10%')).toBeGreaterThan(at('Fee amount'));
      expect(at('Net fee')).toBeGreaterThan(at('Concession: Sibling 10%'));
      expect(at('Tax: GST 18%')).toBeGreaterThan(at('Net fee'));
      expect(at('Total paid')).toBeGreaterThan(at('Tax: GST 18%'));
      expect(h).toContain('&minus; ');                                   // the discount reads as a deduction
      expect(h).not.toContain('GST 18% (18%)');
    });

    it('prints the reason (escaped) and who applied it; a student-side receipt without a reason prints none', () => {
      expect(receiptHtml(discounted)).toContain('Concession reason: second child &lt;b&gt;in&lt;/b&gt; school (applied by Priya)');
      expect(receiptHtml({ ...discounted, concessionReason: null, concessionAppliedByName: null })).not.toContain('Concession reason');
    });

    it('a receipt without concession or tax looks as before; a voided one keeps its concession lines and the VOID marking', () => {
      expect(receiptHtml(receipt)).toContain('Total received');
      expect(receiptHtml(receipt)).not.toContain('Concession');
      expect(receiptHtml(receipt)).not.toContain('Net fee');
      const v = receiptHtml({ ...discounted, status: 'Voided', voidedAt: '2026-10-17T09:00:00Z', voidReason: 'wrong student' });
      expect(v).toContain('Concession: Sibling 10%');
      expect(v).toContain('<div class="watermark">VOID</div>');
    });
  });

  it('a full waiver prints "Total paid ₹0.00" with a Fully waived note, the concession lines and the reason', () => {
    const waived: FeeReceipt = { ...receipt, amount: 0, feeAmount: 0, grossAmount: 1800, netFeeAmount: 0, concessionName: 'Full waiver', concessionType: 'PERCENTAGE', concessionValue: 100, concessionAmount: 1800, totalAmount: 0, concessionReason: 'waived by the principal' };
    const h = receiptHtml(waived);
    expect(h).toContain('Fully waived');
    expect(h).toContain('Total paid');
    expect(h).toContain('₹0.00');
    expect(h).toContain('Concession: Full waiver');
    expect(h).toContain('Concession reason: waived by the principal');
    expect(receiptHtml({ ...waived, concessionAmount: 100, totalAmount: 1700, amount: 1700 })).not.toContain('Fully waived');
  });
});
