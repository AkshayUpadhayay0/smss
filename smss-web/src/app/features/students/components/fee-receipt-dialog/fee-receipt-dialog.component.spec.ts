import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FeeReceipt } from '../../models/fee-payment.model';
import { FeeReceiptDialogComponent } from './fee-receipt-dialog.component';

const base: FeeReceipt = {
  paymentId: 1, receiptNumber: 'RCP-2026-0001', paymentDate: '2026-10-16T08:30:00Z', amount: 1000, paymentMode: 'Cash', collectedByName: 'Office', status: 'Active',
  feeAmount: 1000, taxAmount: 0, totalAmount: 1000,
  school: { name: 'Green Hills School', address: 'Main Road', gstin: '09ABCDE1234F1Z5' },
  student: { studentId: 'GHS001ST001', name: 'Hiral Malik', className: 'Nursery', academicYearName: '2026-27' },
  lines: [{ feeDueId: 1, feeTypeName: 'Tuition', frequencyName: 'Monthly', dueDate: '2026-04-10', amountDue: 1000, allocatedAmount: 1000, paidToDate: 1000 }],
};
const taxed: FeeReceipt = { ...base, amount: 1180, feeAmount: 1000, taxName: 'GST 18%', taxCode: 'GST18', taxRatePercent: 18, taxAmount: 180, totalAmount: 1180 };

describe('FeeReceiptDialogComponent: tax lines and GSTIN', () => {
  let fixture: ComponentFixture<FeeReceiptDialogComponent>;
  const text = () => ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
  const open = (r: FeeReceipt) => {
    fixture = TestBed.createComponent(FeeReceiptDialogComponent);
    fixture.componentRef.setInput('receipt', r);
    fixture.detectChanges();
  };

  it('a tax receipt shows the GSTIN, the fee, the tax (rate not repeated) and the total', () => {
    open(taxed);
    expect(text()).toContain('GSTIN: 09ABCDE1234F1Z5');
    expect(text()).toContain('Tax: GST 18%');
    expect(text()).not.toContain('GST 18% (18%)');
    expect(text()).toContain('Total paid');
  });

  it('shows the rate in brackets when the name does not carry it', () => {
    open({ ...taxed, taxName: 'Service Tax', taxRatePercent: 5, taxAmount: 50 });
    expect(text()).toContain('Tax: Service Tax (5%)');
  });

  it('no GSTIN on a receipt without tax (even if the school has one), none when empty', () => {
    open(base);
    expect(text()).not.toContain('GSTIN');
    expect(text()).toContain('Total received');
    open({ ...taxed, school: { ...taxed.school, gstin: '  ' } });
    expect(text()).not.toContain('GSTIN');
    open({ ...taxed, school: { ...taxed.school, gstin: null } });
    expect(text()).not.toContain('GSTIN');
  });

describe('FeeReceiptDialogComponent: concession lines', () => {
  let fixture: ComponentFixture<FeeReceiptDialogComponent>;
  // the markup with tags turned into spaces, so a label and its amount in neighbouring cells read as "Fee amount ₹1,000.00"
  const text = () => (fixture.nativeElement as HTMLElement).innerHTML.replace(/<[^>]*>/g, ' ').replace(/&minus;/g, '−').replace(/\s+/g, ' ');
  const open = (r: FeeReceipt) => {
    fixture = TestBed.createComponent(FeeReceiptDialogComponent);
    fixture.componentRef.setInput('receipt', r);
    fixture.detectChanges();
  };
  const discounted: FeeReceipt = {
    ...base, amount: 1062, feeAmount: 900, grossAmount: 1000, netFeeAmount: 900, concessionName: 'Sibling 10%', concessionCode: 'SIB10', concessionType: 'PERCENTAGE', concessionValue: 10,
    concessionAmount: 100, concessionReason: 'second child', concessionAppliedByName: 'Priya',
    taxName: 'GST 18%', taxCode: 'GST18', taxRatePercent: 18, taxAmount: 162, totalAmount: 1062,
  };

  it('shows Fee amount, Concession (−), Net fee, Tax and Total paid, then the reason and who applied it', () => {
    open(discounted);
    for (const t of ['Fee amount ₹1,000.00', 'Concession: Sibling 10% − ₹100.00', 'Net fee ₹900.00', 'Tax: GST 18% ₹162.00', 'Total paid ₹1,062.00', 'Concession reason: second child (applied by Priya)']) {
      expect(text()).toContain(t);
    }
  });

  it('a receipt without the reason (the student side) shows the lines but no reason; one without concession or tax is unchanged', () => {
    open({ ...discounted, concessionReason: null, concessionAppliedByName: null });
    expect(text()).toContain('Net fee');
    expect(text()).not.toContain('Concession reason');
    open({ ...base, school: { ...base.school, gstin: null } });
    expect(text()).toContain('Total received');
    expect(text()).not.toContain('Concession');
    expect(text()).not.toContain('Net fee');
  });

  it('a voided receipt keeps its concession lines and says VOID', () => {
    open({ ...discounted, status: 'Voided', voidedAt: '2026-10-17T09:00:00Z', voidReason: 'wrong student' });
    expect(text()).toContain('Concession: Sibling 10%');
    expect(text()).toContain('Voided');
  });

  it('a full waiver shows Total paid ₹0.00 and a Fully waived note', () => {
    open({ ...discounted, amount: 0, feeAmount: 0, netFeeAmount: 0, grossAmount: 1800, concessionAmount: 1800, taxName: null, taxAmount: 0, totalAmount: 0 });
    expect(text()).toContain('Total paid ₹0.00');
    expect(text()).toContain('Fully waived');
  });
});
});

describe('FeeReceiptDialogComponent: late fee line', () => {
  let fixture: ComponentFixture<FeeReceiptDialogComponent>;
  const text = () => ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
  const open = (r: FeeReceipt) => {
    fixture = TestBed.createComponent(FeeReceiptDialogComponent);
    fixture.componentRef.setInput('receipt', r);
    fixture.detectChanges();
  };
  const withLate: FeeReceipt = {
    ...base, amount: 1050, feeAmount: 1000, totalAmount: 1050,
    lateFee: { decision: 'APPLIED', amount: 50, taxAmount: 0, ruleText: '₹50 for each late installment, after 5 grace days', lines: [{ feeType: 'Tuition', dueDate: '2026-04-10', daysLate: 85, daysCharged: 80, baseAmount: 1000, amount: 50 }] },
  };

  it('a receipt without a late fee reads exactly as before (one Total received line, no late fee text)', () => {
    open(base);
    expect(text()).toContain('Total received');
    expect(text()).not.toContain('Late fee');
  });

  it('shows the late fee as its own line with the days late, the rule and the total that includes it', () => {
    open(withLate);
    expect(text()).toContain('Fee amount');
    expect(text()).toContain('Late fee');
    expect(text()).toContain('85 days late, 80 beyond the grace days');
    expect(text()).toContain('Late fee rule: ₹50 for each late installment, after 5 grace days');
    expect(text()).toContain('Total paid');
    expect(text()).not.toContain('Total received');
  });

  it('staff see a waiver with its note, the amount and who decided; the lines are what would have been charged', () => {
    open({ ...base, lateFee: { decision: 'WAIVED', amount: 0, taxAmount: 0, ruleText: '₹50 for each late installment', lines: [{ feeType: 'Tuition', dueDate: '2026-04-10', daysLate: 85, daysCharged: 80, baseAmount: 1000, amount: 50 }], note: 'Poor family', decidedByName: 'Office', waivedAmount: 50 } });
    expect(text()).toContain('Late fee of ₹50.00 waived: Poor family (by Office)');
    expect(text()).toContain('Total received');   // a waived late fee adds no line to the totals
  });
});
