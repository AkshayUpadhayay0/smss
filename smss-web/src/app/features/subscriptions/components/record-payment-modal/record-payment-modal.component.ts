import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { PAYMENT_MODES, SubscriptionInvoice } from '../../models/super-admin-dashboard.model';
import { SuperAdminDashboardService } from '../../services/super-admin-dashboard.service';

const MODE_OPTIONS: SelectOption[] = PAYMENT_MODES.map((m) => ({ label: m, value: m }));
const round2 = (n: number): number => Math.round((n + Number.EPSILON) * 100) / 100;
/** A number input can hand back a string (or null); every amount goes through this before any arithmetic. */
const num = (v: unknown): number => Number(v) || 0;

type Split = 'auto' | 'manual';

/**
 * Records money received from a SCHOOL. One payment can cover several of its unpaid invoices: either "oldest first" (automatic, the
 * server does the split) or a manual split where the Super Admin types how much goes to each invoice. The manual amounts add up to the payment.
 */
@Component({
  selector: 'app-record-payment-modal',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ModalComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './record-payment-modal.component.scss',
  templateUrl: './record-payment-modal.component.html',
})
export class RecordPaymentModalComponent implements OnInit {
  private readonly service = inject(SuperAdminDashboardService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly schoolId = input.required<string>();
  readonly schoolName = input.required<string>();
  /** The invoice the dialog was opened from: starts in manual mode with that invoice settled. */
  readonly preselectInvoiceId = input<number | null>(null);
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly inr = formatInr;
  protected readonly date = formatDate;
  protected readonly modes = MODE_OPTIONS;
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  /** The school's invoices that still have a balance, oldest billing period first. */
  protected readonly open = signal<SubscriptionInvoice[]>([]);
  protected readonly totalOwed = computed(() => round2(this.open().reduce((sum, i) => sum + i.balanceAmount, 0)));

  protected readonly split = new FormControl<Split>('auto', { nonNullable: true });
  protected readonly form = new FormGroup({
    amount: new FormControl<number | null>(null),
    paymentMode: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    referenceNumber: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    remarks: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });
  /** One amount per open invoice, same order as `open()`. Used in manual mode only. */
  protected readonly manual = new FormArray<FormControl<number | null>>([]);

  private readonly mode = toSignal(this.split.valueChanges, { initialValue: 'auto' as Split });
  private readonly typedAmount = toSignal(this.form.controls.amount.valueChanges, { initialValue: null as number | null });
  private readonly manualValues = toSignal(this.manual.valueChanges, { initialValue: [] as (number | null)[] });

  protected readonly isManual = computed(() => this.mode() === 'manual');

  /** What the payment is worth: the typed amount (automatic) or the sum of the manual amounts. */
  protected readonly paymentTotal = computed(() =>
    this.isManual() ? round2(this.manualValues().reduce<number>((s, v) => s + num(v), 0)) : round2(num(this.typedAmount())),
  );

  /** Automatic mode: the split the server will make (oldest first), shown before saving. */
  protected readonly autoPreview = computed(() => {
    let left = this.paymentTotal();
    const rows: { invoice: SubscriptionInvoice; amount: number }[] = [];
    for (const invoice of this.open()) {
      if (left <= 0) break;
      const part = round2(Math.min(left, invoice.balanceAmount));
      rows.push({ invoice, amount: part });
      left = round2(left - part);
    }
    return { rows, leftover: left };
  });

  ngOnInit(): void {
    this.service
      .invoices(this.schoolId(), '', 1, 100)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          const open = page.items
            .filter((i) => i.balanceAmount > 0)
            .sort((a, b) => a.billingPeriodStart.localeCompare(b.billingPeriodStart) || a.invoiceId - b.invoiceId);
          this.open.set(open);
          open.forEach(() => this.manual.push(new FormControl<number | null>(null)));
          const chosen = open.findIndex((i) => i.invoiceId === this.preselectInvoiceId());
          if (chosen >= 0) {
            this.split.setValue('manual');
            this.manual.at(chosen).setValue(open[chosen].balanceAmount);
          }
        },
        error: () => this.loadFailed.set(true),
      });
  }

  protected setSplit(value: Split): void {
    this.split.setValue(value);
  }

  /** The first problem with the amounts, or undefined. Shown under the amounts once the form was submitted. */
  protected amountError(): string | undefined {
    if (!this.submitted()) return undefined;
    const total = this.paymentTotal();
    if (total <= 0) return this.isManual() ? 'Enter an amount for at least one invoice' : 'Enter the amount received';
    if (this.isManual()) {
      const over = this.open().findIndex((i, k) => num(this.manual.at(k).value) > i.balanceAmount);
      if (over >= 0) return `Invoice #${this.open()[over].invoiceId} has only ${formatInr(this.open()[over].balanceAmount)} left to pay`;
      if (this.manual.controls.some((c) => num(c.value) < 0)) return 'Amounts cannot be negative';
    } else if (total > this.totalOwed()) {
      return `More than the ${formatInr(this.totalOwed())} this school owes in total`;
    }
    return undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    // The amount is checked by amountError() (it depends on the split mode); only the other fields use their own validators
    const c = this.form.controls;
    if (c.paymentMode.invalid || c.referenceNumber.invalid || c.remarks.invalid || this.amountError()) return;

    const v = this.form.getRawValue();
    const allocations = this.isManual()
      ? this.open()
          .map((i, k) => ({ invoiceId: i.invoiceId, amount: round2(num(this.manual.at(k).value)) }))
          .filter((a) => a.amount > 0)
      : undefined;

    this.saving.set(true);
    this.service
      .recordPayment({
        schoolId: this.schoolId(),
        amount: this.paymentTotal(),
        paymentMode: v.paymentMode,
        referenceNumber: v.referenceNumber.trim() || null,
        remarks: v.remarks.trim() || null,
        allocations,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (p) => {
          this.toast.success(p.allocations.length > 1 ? `Payment recorded and split over ${p.allocations.length} invoices.` : 'Payment recorded.');
          this.saved.emit();
        },
        error: () => undefined, // reason toasted by the interceptor
      });
  }
}
