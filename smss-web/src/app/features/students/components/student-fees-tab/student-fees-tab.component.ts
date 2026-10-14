import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, ModalComponent, SelectComponent, SelectOption, TableColumn, TableComponent, TextareaComponent } from '../../../../shared/components';
import { StudentFeeConcession, StudentFeeLineRow, StudentFees } from '../../models/student-fees.model';
import { StudentService } from '../../services/student.service';

const LINE_COLUMNS: TableColumn<StudentFeeLineRow>[] = [
  { key: 'feeTypeName', label: 'Fee Head' },
  { key: 'frequencyName', label: 'Frequency' },
  { key: 'amount', label: 'Amount' },
  { key: 'discount', label: 'Concession' },
  { key: 'netAmount', label: 'Net' },
];

/** "15%" or "₹2,000.00". */
const describeValue = (type: 'PERCENTAGE' | 'FIXED', value: number) =>
  type === 'PERCENTAGE' ? `${value.toLocaleString('en-IN', { maximumFractionDigits: 2 })}%` : formatInr(value);

/** The student's Fees tab: assigned structure, fee heads, applied concessions and net payable per frequency. */
@Component({
  selector: 'app-student-fees-tab',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, ModalComponent, SelectComponent, TableComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-fees-tab.component.scss',
  templateUrl: './student-fees-tab.component.html',
})
export class StudentFeesTabComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  readonly studentId = input.required<string>();

  protected readonly fees = signal<StudentFees | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);

  protected readonly lineColumns = LINE_COLUMNS;
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
        next: (f) => this.fees.set(f),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
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
      },
      error: () => undefined, // the server's reason is toasted by the interceptor; the dialog stays open
    });
  }
}
