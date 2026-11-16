import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { formatInr } from '../../../../core/utils/currency';
import { concessionNameWithValue, concessionPaise } from '../../../fee-setup/models/fee-concession.model';
import { TaxType, taxOptionLabel, taxPaise } from '../../../fee-setup/models/tax-type.model';
import { TaxTypeService } from '../../../fee-setup/services/tax-type.service';
import { ButtonComponent, IconComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { ConcessionChoice, FeeReceipt, PAYMENT_MODES, PaymentMode, toPaise } from '../../models/fee-payment.model';
import { StudentFeeDue, dueStatusTone } from '../../models/student-billing.model';
import { StudentService } from '../../services/student.service';

interface AllocRow {
  dueId: number;
  /** The installment's fee type, to match a concession that applies to one fee type only (null on an older server). */
  feeTypeId: number | null;
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
  imports: [ReactiveFormsModule, RouterLink, ModalComponent, ButtonComponent, IconComponent, InputComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './collect-payment-dialog.component.scss',
  templateUrl: './collect-payment-dialog.component.html',
})
export class CollectPaymentDialogComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly taxTypeApi = inject(TaxTypeService);
  private readonly auth = inject(AuthService);

  readonly studentId = input.required<string>();
  /** The student's installments; only those with something outstanding are listed. */
  readonly dues = input.required<StudentFeeDue[]>();

  readonly recorded = output<FeeReceipt>();
  readonly closed = output<void>();

  protected readonly modeOptions: SelectOption[] = PAYMENT_MODES.map((m) => ({ label: m, value: m }));
  protected readonly modeControl = new FormControl<string>('Cash', { nonNullable: true, validators: [Validators.required] });
  protected readonly amountControl = new FormControl('', { nonNullable: true });
  protected readonly remarksControl = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });

  /** '' = no tax (the default); otherwise the chosen tax type's id as text (the select works with strings). */
  protected readonly taxControl = new FormControl<string>('', { nonNullable: true });
  /** The school's ACTIVE tax types (the server lists only those for an Accountant; a School Admin's list is filtered here too). */
  protected readonly taxTypes = signal<TaxType[]>([]);
  protected readonly taxLoaded = signal(false);
  private readonly taxId = signal('');
  protected readonly isSchoolAdmin = computed(() => this.auth.currentUserRoles().includes('School Admin'));
  protected readonly taxOptions = computed<SelectOption[]>(() => [{ label: 'None', value: '' }, ...this.taxTypes().map((t) => ({ label: taxOptionLabel(t), value: String(t.taxTypeId) }))]);
  protected readonly selectedTax = computed(() => this.taxTypes().find((t) => String(t.taxTypeId) === this.taxId()) ?? null);

  /** '' = no concession (the default); otherwise the chosen concession's id as text. ONE per payment. */
  protected readonly concessionControl = new FormControl<string>('', { nonNullable: true });
  protected readonly reasonControl = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });
  /** The school's ACTIVE concessions (the server lists only those). Empty when there are none or the list could not be loaded: the field is then hidden. */
  protected readonly concessions = signal<ConcessionChoice[]>([]);
  private readonly concessionId = signal('');
  private readonly reasonText = signal('');
  protected readonly concessionOptions = computed<SelectOption[]>(() => [
    { label: 'None', value: '' },
    ...this.concessions().map((c) => ({ label: concessionNameWithValue(c.concessionName, c.concessionType, c.value), value: String(c.concessionId) })),
  ]);
  protected readonly selectedConcession = computed(() => this.concessions().find((c) => String(c.concessionId) === this.concessionId()) ?? null);
  protected readonly concessionLabelText = computed(() => {
    const c = this.selectedConcession();
    return c ? concessionNameWithValue(c.concessionName, c.concessionType, c.value) : '';
  });

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
  /**
   * Live preview of the concession, in paise (the server computes the real one with the same rule): the base is the sum of the ticked installments the concession applies to
   * (all of them when it has no fee type), PERCENTAGE rounds half up, FIXED is applied once, never more than the base.
   */
  protected readonly concessionBasePaise = computed(() => {
    const c = this.selectedConcession();
    if (!c) return 0;
    return this.rows()
      .filter((r) => r.checked && !this.rowError(r) && (c.applicableFeeTypeId == null || r.feeTypeId === c.applicableFeeTypeId))
      .reduce((s, r) => s + toPaise(r.amount || 0), 0);
  });
  protected readonly concessionAmountPaise = computed(() => {
    const c = this.selectedConcession();
    return c ? concessionPaise(c.concessionType, c.value, this.concessionBasePaise()) : 0;
  });
  /** The cash fee: the amount applied to installments minus the concession. Tax is worked out on THIS (discount first, then tax). */
  protected readonly netFeePaise = computed(() => this.amountPaise() - this.concessionAmountPaise());
  /** A concession is chosen but none of the ticked installments is one it applies to (shown as a message; Record payment stays disabled). */
  protected readonly concessionNotApplicable = computed(() => !!this.selectedConcession() && this.amountPaise() > 0 && this.allocatedPaise() > 0 && this.concessionBasePaise() === 0);
  protected readonly reasonError = computed(() => {
    if (!this.selectedConcession()) return undefined;
    if (this.reasonText().trim().length === 0) return this.submitted() || this.reasonControl.touched ? 'A reason is required when a concession is selected' : undefined;
    return undefined;
  });
  /** Live preview of the tax (the server computes the real one with the same rule) and of the total to collect, in paise. */
  protected readonly taxAmountPaise = computed(() => {
    const t = this.selectedTax();
    return t ? taxPaise(this.netFeePaise(), t.ratePercent) : 0;
  });
  protected readonly totalToCollectPaise = computed(() => this.netFeePaise() + this.taxAmountPaise());
  protected readonly taxSummaryLabel = computed(() => {
    const t = this.selectedTax();
    return t ? taxOptionLabel(t) : '';
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
  /** With a concession: a written reason, at least one installment it applies to, and something left to collect after the discount. */
  protected readonly concessionOk = computed(() => {
    if (!this.selectedConcession()) return true;
    return this.reasonText().trim().length > 0 && this.reasonControl.valid && this.concessionBasePaise() > 0 && this.netFeePaise() > 0 && this.concessionAmountPaise() > 0;
  });
  protected readonly canSubmit = computed(
    () => !this.saving() && this.amountPaise() > 0 && !this.amountError() && this.difference() === 0 && !this.anyRowError() && this.rows().some((r) => r.checked) && this.modeControl.valid && this.concessionOk(),
  );

  ngOnInit(): void {
    this.rows.set(
      this.dues()
        .filter((d) => d.outstanding > 0)
        .sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.feeDueId - b.feeDueId)
        .map((d) => ({
          dueId: d.feeDueId,
          feeTypeId: d.feeTypeId ?? null,
          label: d.feeTypeName,
          dueDate: d.dueDate,
          status: d.status,
          outstanding: toPaise(d.outstanding),
          checked: false,
          amount: (toPaise(d.outstanding) / 100).toFixed(2),
        })),
    );

    this.taxControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.taxId.set(v));
    this.taxTypeApi
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.taxTypes.set(list.filter((t) => t.isActive));
          this.taxLoaded.set(true);
        },
        // The payment can still be recorded without tax; the interceptor already toasted the reason.
        error: () => this.taxLoaded.set(true),
      });

    this.concessionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.concessionId.set(v));
    this.reasonControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.reasonText.set(v));
    this.students
      .getConcessionChoices()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => this.concessions.set(list),
        error: () => undefined, // a payment can still be recorded without a concession; the interceptor already toasted the reason
      });

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
        // only the chosen type's id: the tax amount is always worked out by the server
        ...(this.selectedTax() ? { taxTypeId: this.selectedTax()!.taxTypeId } : {}),
        // only the chosen concession's id and the written reason: the discount, the tax on the net fee and the total are always worked out by the server
        ...(this.selectedConcession() ? { concessionId: this.selectedConcession()!.concessionId, concessionReason: this.reasonControl.value.trim() } : {}),
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
