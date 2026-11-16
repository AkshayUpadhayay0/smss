import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { extractErrorMessage } from '../../../../core/interceptors/error.interceptor';
import { AuthService } from '../../../../core/services/auth.service';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, IconComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { concessionNameWithValue } from '../../../fee-setup/models/fee-concession.model';
import { TaxType, taxOptionLabel } from '../../../fee-setup/models/tax-type.model';
import { TaxTypeService } from '../../../fee-setup/services/tax-type.service';
import { ConcessionChoice, FeeReceipt, PAYMENT_MODES, PaymentMode, toPaise } from '../../models/fee-payment.model';
import { StudentFeeDue, dueStatusTone } from '../../models/student-billing.model';
import { StudentService } from '../../services/student.service';
import { PayRow, allocateOldestFirst, buildPreview, checkReceived, concessionApplies, isMoney, rowError, rupees, selectedLines } from '../../utils/collect-payment-math';

/**
 * Collect a payment, top to bottom: 1. the installments being paid (tick, amount per line), 2. a concession (only once something is selected, only ones that apply),
 * 3. a tax type (same), then the summary, the payment mode and the amount received, pinned at the bottom with the buttons so the final amount is never scrolled away.
 *
 * Everything money-related shown here is a PREVIEW (see collect-payment-math.ts). The request carries the allocations (their sum is `amount`), the concession id and reason,
 * the tax type id, the mode and the remarks: the server does all the calculations and is the source of truth (the receipt shows ITS figures). "Amount received" and
 * the change are cashier aids only and are never sent.
 */
@Component({
  selector: 'app-collect-payment-dialog',
  imports: [ReactiveFormsModule, RouterLink, ModalComponent, ButtonComponent, IconComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './collect-payment.component.scss',
  templateUrl: './collect-payment.component.html',
})
export class CollectPaymentDialogComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly taxTypeApi = inject(TaxTypeService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  readonly studentId = input.required<string>();
  /** The student's installments; only those with something outstanding are listed. */
  readonly dues = input.required<StudentFeeDue[]>();

  readonly recorded = output<FeeReceipt>();
  readonly closed = output<void>();

  protected readonly inr = formatInr;
  protected readonly tone = dueStatusTone;
  protected readonly modeOptions: SelectOption[] = PAYMENT_MODES.map((m) => ({ label: m, value: m }));

  // ---- 1. installments
  protected readonly rows = signal<PayRow[]>([]);
  private readonly meta = signal<Record<number, { dueDate: string; status: string }>>({});
  protected readonly oldestOpen = signal(false);
  protected readonly oldestText = signal('');
  protected readonly oldestError = signal<string | undefined>(undefined);

  // ---- 2. concession
  protected readonly concessionControl = new FormControl<string>({ value: '', disabled: true }, { nonNullable: true });
  protected readonly reasonControl = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });
  protected readonly concessions = signal<ConcessionChoice[]>([]);
  private readonly concessionId = signal('');
  private readonly reasonText = signal('');
  /** Shown when a chosen concession had to be dropped because the selection changed. */
  protected readonly concessionNotice = signal<string | null>(null);

  // ---- 3. tax
  protected readonly taxControl = new FormControl<string>({ value: '', disabled: true }, { nonNullable: true });
  protected readonly taxTypes = signal<TaxType[]>([]);
  protected readonly taxLoaded = signal(false);
  private readonly taxId = signal('');
  protected readonly isSchoolAdmin = computed(() => this.auth.currentUserRoles().includes('School Admin'));

  protected readonly remarksControl = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });

  // ---- payment
  protected readonly modeControl = new FormControl<string>('Cash', { nonNullable: true, validators: [Validators.required] });
  private readonly mode = signal('Cash');
  /** null = the cashier has not typed: the field follows the total. */
  private readonly receivedRaw = signal<string | null>(null);

  protected readonly saving = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly touchedReason = signal(false);

  // ---- derived
  protected readonly hasSelection = computed(() => selectedLines(this.rows()).length > 0);
  protected readonly anyRowError = computed(() => this.rows().some((r) => !!rowError(r)));
  protected readonly totalOutstanding = computed(() => this.rows().reduce((s, r) => s + r.outstanding, 0));
  protected readonly selectedConcession = computed(() => this.concessions().find((c) => String(c.concessionId) === this.concessionId()) ?? null);
  protected readonly selectedTax = computed(() => this.taxTypes().find((t) => String(t.taxTypeId) === this.taxId()) ?? null);
  protected readonly preview = computed(() => buildPreview(this.rows(), this.selectedConcession(), this.selectedTax()));
  protected readonly waived = computed(() => this.preview().waived);

  /** "None" plus every active concession; the ones that do not apply to the selected installments are shown disabled, with the reason. */
  protected readonly concessionOptions = computed<SelectOption[]>(() => [
    { label: 'None', value: '' },
    ...this.concessions().map((c) => {
      const name = concessionNameWithValue(c.concessionName, c.concessionType, c.value);
      const applies = concessionApplies(c, this.rows());
      return { label: applies ? name : `${name} — applies to ${c.applicableFeeTypeName ?? 'another fee type'} only`, value: String(c.concessionId), disabled: !applies };
    }),
  ]);
  protected readonly taxOptions = computed<SelectOption[]>(() => [{ label: 'None', value: '' }, ...this.taxTypes().map((t) => ({ label: taxOptionLabel(t), value: String(t.taxTypeId) }))]);
  protected readonly concessionLabelText = computed(() => {
    const c = this.selectedConcession();
    return c ? concessionNameWithValue(c.concessionName, c.concessionType, c.value) : '';
  });
  protected readonly taxLabelText = computed(() => {
    const t = this.selectedTax();
    return t ? taxOptionLabel(t) : '';
  });

  protected readonly reasonError = computed(() => (this.selectedConcession() && this.reasonText().trim() === '' && this.touchedReason() ? 'A reason is required when a concession is selected' : undefined));
  protected readonly receivedText = computed(() => this.receivedRaw() ?? rupees(this.preview().totalPaise));
  protected readonly received = computed(() => checkReceived(this.receivedText(), this.preview().totalPaise, this.mode(), this.waived()));
  protected readonly showReceivedError = computed(() => this.receivedRaw() !== null && !this.received().ok);

  protected readonly canSubmit = computed(
    () =>
      !this.saving() &&
      this.hasSelection() &&
      !this.anyRowError() &&
      this.preview().grossPaise > 0 &&
      (!this.selectedConcession() || (this.reasonText().trim() !== '' && this.reasonControl.valid && this.preview().concessionPaise > 0)) &&
      this.remarksControl.valid &&
      this.received().ok,
  );

  constructor() {
    // 2 and 3 stay disabled until at least one installment is selected
    effect(() => {
      const on = this.hasSelection();
      for (const c of [this.concessionControl, this.taxControl]) {
        if (on && c.disabled) c.enable({ emitEvent: false });
        if (!on && c.enabled) c.disable({ emitEvent: false });
      }
    });
    // a chosen concession that no longer applies to the selection is dropped, with a short message
    effect(() => {
      const c = this.selectedConcession();
      if (!c || concessionApplies(c, this.rows())) return;
      this.concessionControl.setValue('');
      this.reasonControl.setValue('');
      this.touchedReason.set(false);
      this.concessionNotice.set(
        this.hasSelection()
          ? `"${c.concessionName}" was removed: it applies to ${c.applicableFeeTypeName ?? 'another fee type'} only, and none of the selected installments is one.`
          : `"${c.concessionName}" was removed because no installment is selected.`,
      );
    });
  }

  ngOnInit(): void {
    const meta: Record<number, { dueDate: string; status: string }> = {};
    const rows = this.dues()
      .filter((d) => d.outstanding > 0)
      .sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.feeDueId - b.feeDueId)
      .map((d) => {
        meta[d.feeDueId] = { dueDate: d.dueDate, status: d.status };
        return { dueId: d.feeDueId, feeTypeId: d.feeTypeId ?? null, feeTypeName: d.feeTypeName, outstanding: toPaise(d.outstanding), checked: false, amount: rupees(toPaise(d.outstanding)) };
      });
    this.meta.set(meta);
    this.rows.set(rows);

    this.concessionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.concessionId.set(v);
      if (v) this.concessionNotice.set(null);
    });
    this.reasonControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.reasonText.set(v));
    this.taxControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.taxId.set(v));
    this.modeControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.mode.set(v);
      this.receivedRaw.set(null);   // a new mode starts again from the total
    });

    this.taxTypeApi
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.taxTypes.set(list.filter((t) => t.isActive));
          this.taxLoaded.set(true);
        },
        error: () => this.taxLoaded.set(true), // a payment can still be recorded without tax; the interceptor already toasted the reason
      });
    this.students
      .getConcessionChoices()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (list) => this.concessions.set(list), error: () => undefined });
  }

  protected date(iso: string): string {
    return formatDate(iso);
  }
  protected dueDate(r: PayRow): string {
    return this.date(this.meta()[r.dueId]?.dueDate ?? '');
  }
  protected status(r: PayRow): string {
    return this.meta()[r.dueId]?.status ?? '';
  }
  protected err(r: PayRow): string | null {
    return rowError(r);
  }
  protected paise(n: number): string {
    return formatInr(Math.max(n, 0) / 100);
  }

  // ---- installments
  protected toggle(r: PayRow, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    // a ticked line starts at what is outstanding (never above it), unless it already holds a valid amount
    this.rows.update((rows) => rows.map((x) => (x.dueId === r.dueId ? { ...x, checked, amount: checked && (rowError({ ...x, checked: true }) || x.amount.trim() === '') ? rupees(x.outstanding) : x.amount } : x)));
  }

  protected setAmount(r: PayRow, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.rows.update((rows) => rows.map((x) => (x.dueId === r.dueId ? { ...x, amount: value, checked: true } : x)));
  }

  protected selectAll(): void {
    this.rows.update((rows) => rows.map((x) => ({ ...x, checked: true, amount: rupees(x.outstanding) })));
  }

  protected clear(): void {
    this.rows.update((rows) => rows.map((x) => ({ ...x, checked: false, amount: rupees(x.outstanding) })));
    this.oldestError.set(undefined);
  }

  protected toggleOldest(): void {
    this.oldestOpen.update((v) => !v);
    this.oldestError.set(undefined);
  }

  /** The optional helper: an amount spread over the installments oldest first, which fills the selection (the lines can still be edited after). */
  protected applyOldest(): void {
    const t = this.oldestText().trim();
    if (t === '' || !isMoney(t) || toPaise(t) < 1) {
      this.oldestError.set('Enter an amount of at least ₹0.01 with at most 2 decimals');
      return;
    }
    if (toPaise(t) > this.totalOutstanding()) {
      this.oldestError.set(`That is more than the ${formatInr(this.totalOutstanding() / 100)} outstanding`);
      return;
    }
    this.oldestError.set(undefined);
    this.rows.set(allocateOldestFirst(this.rows(), toPaise(t)).rows);
  }

  protected onOldestInput(event: Event): void {
    this.oldestText.set((event.target as HTMLInputElement).value);
  }

  // ---- payment
  protected onReceived(event: Event): void {
    this.receivedRaw.set((event.target as HTMLInputElement).value);
  }

  protected useTotal(): void {
    this.receivedRaw.set(null);
  }

  protected submit(): void {
    this.touchedReason.set(true);
    if (this.saving() || !this.canSubmit()) return;   // a second click while saving (or an invalid form) sends nothing
    this.saving.set(true);
    this.serverError.set(null);
    const p = this.preview();
    const c = this.selectedConcession();
    const t = this.selectedTax();
    this.students
      .recordPayment(this.studentId(), {
        // the sum of the allocations: what goes against the installments. No discount, tax or total amount is ever sent: the server works those out.
        amount: p.grossPaise / 100,
        ...(t ? { taxTypeId: t.taxTypeId } : {}),
        ...(c ? { concessionId: c.concessionId, concessionReason: this.reasonControl.value.trim() } : {}),
        paymentMode: this.modeControl.value as PaymentMode,   // a fully waived payment keeps the default mode
        allocations: selectedLines(this.rows()).map((l) => ({ feeDueId: l.row.dueId, amount: l.paise / 100 })),
        remarks: this.remarksControl.value.trim() || null,
      })
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (receipt) => {
          // the receipt shows the server's figures; only note (without any amounts) when the preview was off
          if (toPaise(receipt.totalAmount ?? receipt.amount ?? 0) !== p.totalPaise) console.warn('Collect payment: the preview total differed from the server total; the receipt shows the server figures.');
          this.recorded.emit(receipt);
        },
        // the interceptor toasts the reason too; it stays visible here (e.g. the double-discount guard's 409) and the dialog stays open
        error: (e: unknown) => this.serverError.set(e instanceof HttpErrorResponse ? extractErrorMessage(e) : 'The payment could not be recorded.'),
      });
  }
}
