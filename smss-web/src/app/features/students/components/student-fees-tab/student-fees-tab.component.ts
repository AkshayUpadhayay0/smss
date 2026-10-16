import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, finalize, forkJoin } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, ModalComponent, SelectComponent, SelectOption, TableColumn, TableComponent, TextareaComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { FeeDueRow, FeeItemRow, StudentFeeDues, StudentFeeItem, StudentFeeItems, dueStatusTone } from '../../models/student-billing.model';
import { StudentFeeConcession, StudentFeeLineRow, StudentFees } from '../../models/student-fees.model';
import { CollectPaymentDialogComponent } from '../collect-payment-dialog/collect-payment-dialog.component';
import { FeeReceiptDialogComponent } from '../fee-receipt-dialog/fee-receipt-dialog.component';
import { FEE_MANAGER_ROLES, FeeReceipt, PaymentRow, PaymentSummary, paymentStatusTone } from '../../models/fee-payment.model';
import { AuthService } from '../../../../core/services/auth.service';
import { StudentService } from '../../services/student.service';

const LINE_COLUMNS: TableColumn<StudentFeeLineRow>[] = [
  { key: 'feeTypeName', label: 'Fee Head' },
  { key: 'frequencyName', label: 'Frequency' },
  { key: 'amount', label: 'Amount' },
  { key: 'discount', label: 'Concession' },
  { key: 'netAmount', label: 'Net' },
];

const ITEM_COLUMNS: TableColumn<FeeItemRow>[] = [
  { key: 'feeTypeName', label: 'Fee Head' },
  { key: 'frequencyName', label: 'Pays' },
  { key: 'applicable', label: 'Applies', type: 'badge' },
  { key: 'netAmount', label: 'Net / Period' },
  { key: 'annualNet', label: 'Net / Year' },
  { key: 'dues', label: 'Installments' },
  { key: 'nextDue', label: 'Next Due' },
];

const DUE_COLUMNS: TableColumn<FeeDueRow>[] = [
  { key: 'dueDate', label: 'Due Date' },
  { key: 'feeTypeName', label: 'Fee Head' },
  { key: 'amountDue', label: 'Amount Due' },
  { key: 'amountPaid', label: 'Paid' },
  { key: 'status', label: 'Status', type: 'badge', tone: dueStatusTone },
];

const PAYMENT_COLUMNS: TableColumn<PaymentRow>[] = [
  { key: 'receiptNumber', label: 'Receipt No.' },
  { key: 'paymentDate', label: 'Date' },
  { key: 'amount', label: 'Amount' },
  { key: 'paymentMode', label: 'Mode' },
  { key: 'collectedByName', label: 'Collected By' },
  { key: 'status', label: 'Status', type: 'badge', tone: paymentStatusTone },
];

/** "15%" or "₹2,000.00". */
const describeValue = (type: 'PERCENTAGE' | 'FIXED', value: number) =>
  type === 'PERCENTAGE' ? `${value.toLocaleString('en-IN', { maximumFractionDigits: 2 })}%` : formatInr(value);

/** The student's Fees tab: assigned structure, fee heads, applied concessions and net payable per frequency. */
@Component({
  selector: 'app-student-fees-tab',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, CollectPaymentDialogComponent, FeeReceiptDialogComponent, IconComponent, ModalComponent, SelectComponent, TableComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-fees-tab.component.scss',
  templateUrl: './student-fees-tab.component.html',
})
export class StudentFeesTabComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);

  readonly studentId = input.required<string>();

  protected readonly fees = signal<StudentFees | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);

  protected readonly lineColumns = LINE_COLUMNS;
  protected readonly itemColumns = ITEM_COLUMNS;
  protected readonly dueColumns = DUE_COLUMNS;
  protected readonly paymentColumns = PAYMENT_COLUMNS;

  // ---- payments
  protected readonly payments = signal<PaymentSummary[]>([]);
  protected readonly paymentsFailed = signal(false);
  protected readonly collectOpen = signal(false);
  /** The receipt on screen: just recorded, or reopened from the history. */
  protected readonly receipt = signal<{ data: FeeReceipt; justPaid: boolean } | null>(null);
  protected readonly receiptLoading = signal<number | null>(null);

  // ---- void a payment
  protected readonly canVoid = computed(() => this.auth.currentUserRoles().some((r) => (FEE_MANAGER_ROLES as readonly string[]).includes(r)));
  protected readonly voiding = signal<PaymentRow | null>(null);
  protected readonly voidReasonControl = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500)] });
  /** Submit stays disabled until there is a real (non-blank) reason. */
  protected readonly voidReasonText = toSignal(this.voidReasonControl.valueChanges, { initialValue: '' });
  protected readonly voidReady = computed(() => this.voidReasonText().trim().length > 0 && !this.busy());

  /** Collecting needs at least one installment with something left to pay. */
  protected readonly canCollect = computed(() => (this.dues()?.dues ?? []).some((d) => d.outstanding > 0));

  protected readonly paymentRows = computed<PaymentRow[]>(() =>
    this.payments().map((p) => ({
      paymentId: p.paymentId,
      receiptNumber: p.receiptNumber,
      paymentDate: new Date(p.paymentDate).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }),
      amount: formatInr(p.amount),
      paymentMode: p.paymentMode,
      collectedByName: p.collectedByName ?? '—',
      status: p.status,
    })),
  );

  // ---- fee items and the installment schedule
  protected readonly items = signal<StudentFeeItems | null>(null);
  protected readonly dues = signal<StudentFeeDues | null>(null);
  protected readonly billingFailed = signal(false);

  // ---- edit-item dialog
  protected readonly editing = signal<StudentFeeItem | null>(null);
  protected readonly frequencyControl = new FormControl('', { nonNullable: true });
  protected readonly applicableControl = new FormControl(true, { nonNullable: true });

  protected readonly itemRows = computed<FeeItemRow[]>(() =>
    (this.items()?.items ?? []).map((i) => ({
      studentFeeItemId: i.studentFeeItemId,
      feeTypeName: i.feeTypeName,
      frequencyName: i.frequencyName === i.structureFrequencyName ? i.frequencyName : `${i.frequencyName} (structure: ${i.structureFrequencyName})`,
      applicable: i.isApplicable ? 'Yes' : 'No',
      netAmount: `${formatInr(i.netAmount)} / ${i.structureFrequencyName}`,
      annualNet: i.isApplicable ? formatInr(i.annualNetAmount) : '—',
      dues: i.isApplicable ? String(i.dueCount) : '—',
      nextDue: i.isApplicable && i.nextDueDate ? formatDate(i.nextDueDate) : '—',
    })),
  );

  protected readonly dueRows = computed<FeeDueRow[]>(() =>
    (this.dues()?.dues ?? []).map((d) => ({
      feeDueId: d.feeDueId,
      dueDate: formatDate(d.dueDate),
      feeTypeName: d.feeTypeName,
      amountDue: formatInr(d.amountDue),
      amountPaid: formatInr(d.amountPaid),
      status: d.status,
    })),
  );

  protected readonly yearTotal = computed(() => (this.items() ? formatInr(this.items()!.annualNetTotal) : ''));

  protected readonly dueSummary = computed(() => {
    const d = this.dues();
    if (!d) return null;
    return { total: formatInr(d.totalDue), paid: formatInr(d.totalPaid), outstanding: formatInr(d.outstanding), overdue: formatInr(d.overdueAmount), overdueCount: d.overdueCount };
  });

  protected readonly editFrequencyOptions = computed<SelectOption[]>(() =>
    (this.editing()?.allowedFrequencies ?? []).map((f) => ({ label: f.frequencyName, value: String(f.frequencyId) })),
  );
  protected readonly describeValue = describeValue;

  // ---- assign dialog
  protected readonly assignOpen = signal(false);
  protected readonly showOtherClasses = signal(false);
  protected readonly structureControl = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  protected readonly structureError = signal<string | undefined>(undefined);

  // ---- concession dialog
  protected readonly concessionOpen = signal(false);
  protected readonly concessionControl = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  protected readonly remarksControl = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });
  protected readonly concessionError = signal<string | undefined>(undefined);

  protected readonly lineRows = computed<StudentFeeLineRow[]>(() =>
    (this.fees()?.lines ?? []).map((l) => ({
      feeTypeName: l.feeTypeName,
      frequencyName: l.frequencyName,
      amount: formatInr(l.amount),
      discount: l.discount > 0 ? '− ' + formatInr(l.discount) : '—',
      netAmount: formatInr(l.netAmount),
    })),
  );

  protected readonly totals = computed(() =>
    (this.fees()?.totals ?? []).map((t) => ({
      frequencyName: t.frequencyName,
      gross: formatInr(t.gross),
      concession: t.concession > 0 ? '− ' + formatInr(t.concession) : '—',
      net: formatInr(t.netPayable),
    })),
  );

  /** Structures offered for assigning: the student's own class first/only, unless "other classes" is ticked. */
  protected readonly structureOptions = computed<SelectOption[]>(() => {
    const f = this.fees();
    if (!f) return [];
    const mine = (f.availableStructures ?? []).filter((s) => s.classId === f.classId);
    const list = this.showOtherClasses() ? f.availableStructures : mine;
    return list.map((s) => ({ label: this.showOtherClasses() ? `${s.className} — ${s.structureName}` : s.structureName, value: String(s.feeStructureId) }));
  });

  protected readonly hasOtherClassStructures = computed(() => {
    const f = this.fees();
    return !!f && f.availableStructures.some((s) => s.classId !== f.classId);
  });

  protected readonly concessionOptions = computed<SelectOption[]>(() =>
    (this.fees()?.availableConcessions ?? []).map((c) => ({
      label: `${c.concessionName} — ${describeValue(c.concessionType, c.value)}${c.applicableFeeTypeName ? ' on ' + c.applicableFeeTypeName : ''}`,
      value: String(c.concessionId),
    })),
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.students
      .getFees(this.studentId())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (f) => {
          this.fees.set(f);
          this.loadBilling(f);
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  /** The student's fee items and installments (only meaningful once a fee structure is assigned). */
  private loadBilling(f: StudentFees | null = this.fees()): void {
    this.loadPayments();
    if (!f?.feeStructureId) {
      this.items.set(null);
      this.dues.set(null);
      return;
    }
    this.billingFailed.set(false);
    forkJoin({ items: this.students.getFeeItems(this.studentId()), dues: this.students.getFeeDues(this.studentId()) })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.items.set(r.items);
          this.dues.set(r.dues);
        },
        error: () => this.billingFailed.set(true), // already toasted by the interceptor
      });
  }

  private loadPayments(): void {
    this.paymentsFailed.set(false);
    this.students
      .getPayments(this.studentId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => this.payments.set(p),
        error: () => this.paymentsFailed.set(true), // already toasted by the interceptor
      });
  }

  // ---- collect a payment / reopen a receipt
  protected openCollect(): void {
    this.collectOpen.set(true);
  }

  protected onRecorded(receipt: FeeReceipt): void {
    this.collectOpen.set(false);
    this.receipt.set({ data: receipt, justPaid: true });
    this.toast.success(`Payment recorded. Receipt ${receipt.receiptNumber}.`);
    this.refreshAfterChange();   // the installments' statuses move
    this.loadPayments();
  }

  protected viewReceipt(row: PaymentRow): void {
    if (this.receiptLoading() !== null) return;
    this.receiptLoading.set(row.paymentId);
    this.students
      .getReceipt(row.paymentId)
      .pipe(finalize(() => this.receiptLoading.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => this.receipt.set({ data: r, justPaid: false }),
        error: () => undefined, // already toasted by the interceptor
      });
  }

  protected openVoid(row: PaymentRow): void {
    this.voidReasonControl.reset('');
    this.voiding.set(row);
  }

  protected submitVoid(): void {
    const row = this.voiding();
    const reason = this.voidReasonControl.value.trim();
    if (!row || !reason || this.busy()) return;
    this.busy.set(true);
    this.students
      .voidPayment(row.paymentId, reason)
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.voiding.set(null);
          this.toast.success(`Receipt ${row.receiptNumber} voided.`);
          this.loadPayments();
          this.refreshAfterChange(); // the installments it had paid go back to Overdue / Pending
        },
        error: () => undefined, // the server's reason is toasted by the interceptor; the dialog stays open
      });
  }

  // ---- edit one fee item (frequency / applicability)
  protected openEdit(row: FeeItemRow): void {
    const item = this.items()?.items.find((i) => i.studentFeeItemId === row.studentFeeItemId);
    if (!item) return;
    this.frequencyControl.setValue(String(item.frequencyId));
    this.applicableControl.setValue(item.isApplicable);
    this.editing.set(item);
  }

  protected submitEdit(): void {
    const item = this.editing();
    if (!item || this.busy()) return;
    const body: { frequencyId?: number; isApplicable?: boolean } = {};
    if (Number(this.frequencyControl.value) !== item.frequencyId) body.frequencyId = Number(this.frequencyControl.value);
    if (this.applicableControl.value !== item.isApplicable) body.isApplicable = this.applicableControl.value;
    if (Object.keys(body).length === 0) {
      this.editing.set(null);
      return;
    }
    this.busy.set(true);
    this.students
      .updateFeeItem(this.studentId(), item.studentFeeItemId, body)
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.items.set(r);
          this.editing.set(null);
          this.toast.success('Fee item updated. Unpaid installments were regenerated.');
          this.refreshAfterChange();
        },
        error: () => undefined, // the server's reason is toasted by the interceptor; the dialog stays open
      });
  }

  /** The schedule and the concession-adjusted totals both move when an item changes. */
  private refreshAfterChange(): void {
    this.students
      .getFees(this.studentId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (f) => this.fees.set(f), error: () => undefined });
    this.students
      .getFeeDues(this.studentId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (d) => this.dues.set(d), error: () => undefined });
  }

  protected appliesTo(c: StudentFeeConcession): string {
    return c.applicableFeeTypeName ?? 'All fee heads';
  }

  // ---- assign
  protected openAssign(): void {
    this.structureControl.reset('');
    this.structureError.set(undefined);
    this.showOtherClasses.set(false);
    this.assignOpen.set(true);
  }

  protected submitAssign(): void {
    if (this.busy()) return;
    if (this.structureControl.invalid) {
      this.structureError.set('Select a fee structure');
      return;
    }
    this.run(this.students.assignFeeStructure(this.studentId(), Number(this.structureControl.value)), 'Fee structure assigned.', () => this.assignOpen.set(false));
  }

  // ---- concessions
  protected openConcession(): void {
    this.concessionControl.reset('');
    this.remarksControl.reset('');
    this.concessionError.set(undefined);
    this.concessionOpen.set(true);
  }

  protected submitConcession(): void {
    if (this.busy()) return;
    if (this.concessionControl.invalid) {
      this.concessionError.set('Select a concession');
      return;
    }
    if (this.remarksControl.invalid) return;
    this.run(
      this.students.applyConcession(this.studentId(), Number(this.concessionControl.value), this.remarksControl.value.trim() || null),
      'Concession added.',
      () => this.concessionOpen.set(false),
    );
  }

  protected async removeConcession(c: StudentFeeConcession): Promise<void> {
    const ok = await this.confirmDialog.confirm({
      title: 'Remove concession?',
      message: `"${c.concessionName}" will be removed from this student and the net payable will go back up.`,
      confirmText: 'Remove',
      variant: 'danger',
    });
    if (!ok) return;
    this.run(this.students.removeConcession(this.studentId(), c.concessionId), 'Concession removed.');
  }

  private run(request$: Observable<StudentFees>, success: string, after?: () => void): void {
    this.busy.set(true);
    request$.pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (f) => {
        this.fees.set(f);
        after?.();
        this.toast.success(success);
        this.loadBilling(f); // a new structure / concession changes the items and the installments
      },
      error: () => undefined, // the server's reason is toasted by the interceptor; the dialog stays open
    });
  }
}
