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
});
