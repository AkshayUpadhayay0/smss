import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { SubscriptionInvoice, SubscriptionPayment, invoiceTone } from '../../models/super-admin-dashboard.model';
import { SuperAdminDashboardService } from '../../services/super-admin-dashboard.service';

/** One invoice in full: the locked counts and rate, the amounts, and every payment (voided ones struck through). Active payments can be voided with a reason. */
@Component({
  selector: 'app-invoice-detail-modal',
  imports: [ReactiveFormsModule, ButtonComponent, ModalComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './invoice-detail-modal.component.scss',
  templateUrl: './invoice-detail-modal.component.html',
})
export class InvoiceDetailModalComponent implements OnInit {
  private readonly service = inject(SuperAdminDashboardService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly invoiceId = input.required<number>();
  /** Asks the page to open the payment dialog for this invoice. */
  readonly pay = output<SubscriptionInvoice>();
  /** A payment was voided, so the numbers behind the dashboard changed. */
  readonly changed = output<void>();
  readonly closed = output<void>();

  protected readonly inr = formatInr;
  protected readonly date = formatDate;
  protected readonly tone = invoiceTone;

  protected readonly invoice = signal<SubscriptionInvoice | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly voidingId = signal<number | null>(null);
  protected readonly voidReason = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500)] });
  protected readonly voidSubmitted = signal(false);
  protected readonly voidBusy = signal(false);

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.service
      .invoice(this.invoiceId())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (i) => { this.invoice.set(i); this.failed.set(false); }, error: () => this.failed.set(true) });
  }

  /** How much of this payment went to the invoice being shown. */
  protected toThisInvoice(p: SubscriptionPayment, invoiceId: number): number {
    return p.allocations.find((a) => a.invoiceId === invoiceId)?.allocatedAmount ?? p.amount;
  }

  protected otherInvoices(p: SubscriptionPayment, invoiceId: number): string {
    return p.allocations.filter((a) => a.invoiceId !== invoiceId).map((a) => `#${a.invoiceId}`).join(', ');
  }

  protected startVoid(p: SubscriptionPayment): void {
    this.voidingId.set(p.paymentId);
    this.voidReason.reset('');
    this.voidSubmitted.set(false);
  }

  protected cancelVoid(): void {
    this.voidingId.set(null);
  }

  protected confirmVoid(): void {
    const id = this.voidingId();
    if (id === null || this.voidBusy()) return;
    this.voidSubmitted.set(true);
    const reason = this.voidReason.value.trim();
    if (!reason) return;
    this.voidBusy.set(true);
    this.service
      .voidPayment(id, reason)
      .pipe(finalize(() => this.voidBusy.set(false)))
      .subscribe({
        next: () => {
          this.toast.success('Payment voided.');
          this.voidingId.set(null);
          this.changed.emit();
          this.load();
        },
        error: () => undefined, // reason toasted by the interceptor
      });
  }
}
