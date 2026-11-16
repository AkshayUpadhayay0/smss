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
});
