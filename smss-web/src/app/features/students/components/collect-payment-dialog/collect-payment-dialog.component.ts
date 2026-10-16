import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, IconComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { FeeReceipt, PAYMENT_MODES, PaymentMode, toPaise } from '../../models/fee-payment.model';
import { StudentFeeDue, dueStatusTone } from '../../models/student-billing.model';
import { StudentService } from '../../services/student.service';

interface AllocRow {
  dueId: number;
  label: string;
  dueDate: string;
  status: string;
  /** Still owed on this installment, in paise. */
  outstanding: number;
  checked: boolean;
  /** What the user typed for this installment (rupees). */
  amount: string;
}

/**
 * Collect a payment: mode, amount, remarks and how the amount is split over the student's outstanding installments (oldest first).
 * Typing the amount fills the split in oldest-first; ticking boxes / editing the amounts overrides that. Submit is only possible when the split
 * adds up to exactly the amount. Nothing is sent as "paid" by the browser: the server records the payment and works the status out.
 */
@Component({
  selector: 'app-collect-payment-dialog',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, IconComponent, InputComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './collect-payment-dialog.component.scss',
  templateUrl: './collect-payment-dialog.component.html',
})
export class CollectPaymentDialogComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly destroyRef = inject(DestroyRef);

  readonly studentId = input.required<string>();
  /** The student's installments; only those with something outstanding are listed. */
  readonly dues = input.required<StudentFeeDue[]>();

  readonly recorded = output<FeeReceipt>();
  readonly closed = output<void>();

  protected readonly modeOptions: SelectOption[] = PAYMENT_MODES.map((m) => ({ label: m, value: m }));
  protected readonly modeControl = new FormControl<string>('Cash', { nonNullable: true, validators: [Validators.required] });
  protected readonly amountControl = new FormControl('', { nonNullable: true });
  protected readonly remarksControl = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });

  protected readonly rows = signal<AllocRow[]>([]);
  protected readonly amountText = signal('');
  /** The user changed the split themselves: stop re-splitting automatically when the amount changes. */
  protected readonly manual = signal(false);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly inr = formatInr;
  protected readonly tone = dueStatusTone;

  protected readonly amountPaise = computed(() => {
    const t = this.amountText().trim();
    return t === '' || !Number.isFinite(Number(t)) ? 0 : toPaise(t);
  });
  protected readonly totalOutstanding = computed(() => this.rows().reduce((s, r) => s + r.outstanding, 0));
  protected readonly allocatedPaise = computed(() => this.rows().filter((r) => r.checked).reduce((s, r) => s + (this.rowError(r) ? 0 : toPaise(r.amount || 0)), 0));
  protected readonly difference = computed(() => this.amountPaise() - this.allocatedPaise());

  protected readonly amountError = computed(() => {
    const t = this.amountText().trim();
    if (!(this.submitted() || t !== '')) return undefined;
    if (t === '' || this.amountPaise() <= 0) return 'Enter an amount greater than 0';
    if (!/^\d+(\.\d{1,2})?$/.test(t)) return 'Use at most 2 decimal places';
    if (this.amountPaise() > this.totalOutstanding()) return `More than the ${formatInr(this.totalOutstanding() / 100)} outstanding`;
    return undefined;
  });

  protected readonly anyRowError = computed(() => this.rows().some((r) => r.checked && !!this.rowError(r)));
  protected readonly canSubmit = computed(
    () => !this.saving() && this.amountPaise() > 0 && !this.amountError() && this.difference() === 0 && !this.anyRowError() && this.rows().some((r) => r.checked) && this.modeControl.valid,
  );

  ngOnInit(): void {
    this.rows.set(
      this.dues()
        .filter((d) => d.outstanding > 0)
        .sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.feeDueId - b.feeDueId)
        .map((d) => ({
          dueId: d.feeDueId,
          label: d.feeTypeName,
          dueDate: d.dueDate,
          status: d.status,
          outstanding: toPaise(d.outstanding),
          checked: false,
          amount: (toPaise(d.outstanding) / 100).toFixed(2),
        })),
    );

    this.amountControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.amountText.set(v);
      if (!this.manual()) this.autoAllocate();
    });
  }

  protected date(iso: string): string {
    return formatDate(iso);
  }

  /** The error for a ticked row, or null. */
  protected rowError(r: AllocRow): string | null {
    if (!r.checked) return null;
    const t = String(r.amount ?? '').trim();
    if (t === '' || !Number.isFinite(Number(t)) || toPaise(t) <= 0) return 'Enter an amount';
    if (!/^\d+(\.\d{1,2})?$/.test(t)) return 'At most 2 decimals';
    if (toPaise(t) > r.outstanding) return `Only ${formatInr(r.outstanding / 100)} is outstanding`;
    return null;
  }

  /** Fill the split oldest-first from the amount typed. */
  protected autoAllocate(): void {
    let left = this.amountPaise();
    this.rows.update((rows) =>
      rows.map((r) => {
        const take = Math.min(Math.max(left, 0), r.outstanding);
        left -= take;
        return take > 0 ? { ...r, checked: true, amount: (take / 100).toFixed(2) } : { ...r, checked: false, amount: (r.outstanding / 100).toFixed(2) };
      }),
    );
  }

  protected allocateAgain(): void {
    this.manual.set(false);
    this.autoAllocate();
  }

  protected payEverything(): void {
    this.amountControl.setValue((this.totalOutstanding() / 100).toFixed(2));   // the subscription re-splits it
  }

  protected toggle(r: AllocRow, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.manual.set(true);
    // a ticked row starts at its remaining balance, which the user can lower
    this.rows.update((rows) => rows.map((x) => (x.dueId === r.dueId ? { ...x, checked, amount: checked ? (x.outstanding / 100).toFixed(2) : x.amount } : x)));
  }

  protected setAmount(r: AllocRow, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.manual.set(true);
    this.rows.update((rows) => rows.map((x) => (x.dueId === r.dueId ? { ...x, amount: value, checked: true } : x)));
  }

  protected submit(): void {
    this.submitted.set(true);
    if (!this.canSubmit()) return;
    this.saving.set(true);
    this.students
      .recordPayment(this.studentId(), {
        amount: this.amountPaise() / 100,
        paymentMode: this.modeControl.value as PaymentMode,
        allocations: this.rows().filter((r) => r.checked).map((r) => ({ feeDueId: r.dueId, amount: toPaise(r.amount) / 100 })),
        remarks: this.remarksControl.value.trim() || null,
      })
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (receipt) => this.recorded.emit(receipt),
        error: () => undefined, // the server's reason (e.g. an installment was just paid by someone else) is toasted; the dialog stays open
      });
  }
}
