import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { formatInr } from '../../../../core/utils/currency';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { FeeReceipt, ReceiptTotalLine, hasConcession, lateFeeNotes, hasTax, isFullyWaived, receiptTotals, taxLabel } from '../../models/fee-payment.model';
import { printReceipt } from '../../utils/receipt-print';

/** A receipt on screen, with Print / Save as PDF. Used right after collecting a payment and when reopening one from the history. */
@Component({
  selector: 'app-fee-receipt-dialog',
  imports: [ModalComponent, ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './fee-receipt-dialog.component.scss',
  template: `
    <app-modal [title]="'Receipt ' + receipt().receiptNumber" (closed)="closed.emit()">
      <article class="receipt" [class.voided]="isVoided()" aria-label="Fee receipt">
        @if (isVoided()) {
          <div class="void-banner" role="alert">
            <strong>VOID</strong>
            <span>
              Voided{{ receipt().voidedAt ? ' on ' + voidedWhen() : '' }}{{ receipt().voidedByName ? ' by ' + receipt().voidedByName : '' }}.
              Reason: {{ receipt().voidReason }}. This receipt is no longer valid.
            </span>
          </div>
          <span class="watermark" aria-hidden="true">VOID</span>
        }
        @if (justPaid()) {
          <p class="done"><app-icon name="circle-check" [size]="18" /> Payment recorded.</p>
        }
        <header class="top">
          <div>
            <h3>{{ receipt().school.name }}</h3>
            @if (receipt().school.address) {
              <p class="muted">{{ receipt().school.address }}</p>
            }
            @if (gstin(); as g) {
              <p class="muted">GSTIN: {{ g }}</p>
            }
          </div>
          <div class="no">
            <strong>{{ receipt().receiptNumber }}</strong>
            <span class="muted">{{ when() }}</span>
          </div>
        </header>

        <dl class="meta">
          <div><dt>Student</dt><dd>{{ receipt().student.name }}</dd></div>
          <div><dt>Student ID</dt><dd>{{ receipt().student.studentId }}</dd></div>
          <div><dt>Class</dt><dd>{{ receipt().student.className || '—' }}</dd></div>
          <div><dt>Payment mode</dt><dd>{{ receipt().paymentMode }}</dd></div>
        </dl>

        <table>
          <thead>
            <tr><th>Fee</th><th>Due date</th><th class="num">Installment</th><th class="num">Paid now</th></tr>
          </thead>
          <tbody>
            @for (l of receipt().lines; track l.feeDueId) {
              <tr>
                <td>{{ l.feeTypeName }}<small>{{ l.frequencyName }}@if ((receipt().student.academicYearNames?.length ?? 0) > 1 && l.academicYearName) { · {{ l.academicYearName }} }</small></td>
                <td>{{ date(l.dueDate) }}</td>
                <td class="num">{{ inr(l.amountDue) }}</td>
                <td class="num"><strong>{{ inr(l.allocatedAmount) }}</strong></td>
              </tr>
            }
          </tbody>
          <tfoot>
            @for (t of totals(); track t.label) {
              <tr [class.deduct]="t.deduction"><td colspan="3">{{ t.label }}</td><td class="num">{{ t.deduction ? '− ' : '' }}{{ inr(t.amount) }}</td></tr>
            }
          </tfoot>
        </table>

        @for (n of lateNotes(); track n) {
          <p class="muted" data-late-note>{{ n }}</p>
        }
        @if (waived()) {
          <p class="muted"><strong>Fully waived</strong>: nothing was collected on this receipt.</p>
        }
        @if (concessionReason(); as why) {
          <p class="muted">Concession reason: {{ why }}@if (receipt().concessionAppliedByName) { (applied by {{ receipt().concessionAppliedByName }}) }</p>
        }
        @if (receipt().remarks) {
          <p class="muted">Remarks: {{ receipt().remarks }}</p>
        }
        @if (receipt().collectedByName) {
          <p class="muted">Received by {{ receipt().collectedByName }}</p>
        }
      </article>

      <ng-container modal-footer>
        <app-button variant="secondary" (click)="closed.emit()">Close</app-button>
        <app-button (click)="print()"><app-icon name="file-text" [size]="16" />Print / Save as PDF</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class FeeReceiptDialogComponent {
  private readonly toast = inject(ToastService);

  readonly receipt = input.required<FeeReceipt>();
  /** True right after the payment was recorded (shows the confirmation line). */
  readonly justPaid = input(false);
  readonly closed = output<void>();

  protected readonly inr = formatInr;
  protected readonly date = formatDate;

  protected taxed(): boolean {
    return hasTax(this.receipt());
  }

  /** The school's GSTIN, only on a receipt that carries tax (the API already sends it that way; this keeps the rule in one more place). */
  protected gstin(): string | null {
    const g = this.receipt().school.gstin?.trim();
    return g && hasTax(this.receipt()) ? g : null;
  }

  /** The totals block: Fee amount / Concession / Net fee / Tax / Total paid (only the lines that apply), or just "Total received" as before. */
  protected totals(): ReceiptTotalLine[] {
    return receiptTotals(this.receipt());
  }

  /** What explains the late fee: each late installment and the rule, or the waiver with its note and who decided. */
  protected lateNotes(): string[] {
    return lateFeeNotes(this.receipt(), this.inr);
  }

  protected waived(): boolean {
    return isFullyWaived(this.receipt());
  }

  /** Why a concession was given: staff see it, the student's own receipt does not carry it. */
  protected concessionReason(): string | null {
    const r = this.receipt();
    return hasConcession(r) && r.concessionReason?.trim() ? r.concessionReason.trim() : null;
  }

  protected taxText(): string {
    return taxLabel(this.receipt());
  }

  protected isVoided(): boolean {
    return this.receipt().status === 'Voided';
  }

  protected voidedWhen(): string {
    return new Date(this.receipt().voidedAt!).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  protected when(): string {
    return new Date(this.receipt().paymentDate).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  protected print(): void {
    if (!printReceipt(this.receipt())) this.toast.warning('The browser blocked the print window. Allow pop-ups for this site and try again.');
  }
}
