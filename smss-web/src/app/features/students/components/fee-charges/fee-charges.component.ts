import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { CardComponent, IconComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { FeeItemSelection, FeePreview, FeePreviewLine, InstallmentPlanOption } from '../../models/student-billing.model';
import { StudentService } from '../../services/student.service';

/** What a back-dated "charge fees from" date would create for the chosen plans: shown to, and confirmed by, the person admitting the student. */
export interface BackdatingSummary {
  /** The charge-from date is before today. */
  isBackdated: boolean;
  /** False when a back-dated charge is not possible (not the current academic year ...); `message` says why. */
  allowed: boolean;
  message: string | null;
  /** Installments dated before today that would be created, and their total. */
  count: number;
  amount: number;
}

/**
 * "Fee Configuration" for the admission forms. Once an academic year and class are chosen it shows the fee heads of that class's fee structure: the billing cycle and the amount
 * PER CYCLE (set by the fee structure, not by the student), an "Installment plan" where the fee allows one (None by default), the amount of each installment and what is payable from the
 * charge-from date. Nothing is saved here: the choices are emitted and sent with the admission request.
 */
@Component({
  selector: 'app-fee-configuration',
  imports: [NgTemplateOutlet, CardComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './fee-charges.component.scss',
  templateUrl: './fee-charges.component.html',
})
export class FeeConfigurationComponent {
  private readonly students = inject(StudentService);
  private readonly destroyRef = inject(DestroyRef);

  readonly academicYearId = input<number | null>(null);
  readonly classId = input<number | null>(null);
  /** The date fees are charged from (yyyy-MM-dd). Empty = today. Fees are owed from the first due date on or after it, and never before today unless the date is an earlier, confirmed one. */
  readonly startDate = input<string | null>(null);
  /** Wrap in a card (the onboarding form) or render bare (inside a dialog). */
  readonly framed = input(true);

  /** The choices, one per fee head, whenever they change (and when the structure first loads). */
  readonly selectionsChange = output<FeeItemSelection[]>();
  /** What an earlier charge-from date would create, whenever it changes (the parent asks for a confirmation and a reason). */
  readonly backdatingChange = output<BackdatingSummary>();

  protected readonly preview = signal<FeePreview | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  /** fee type id -> chosen installment plan code (NONE when absent). */
  protected readonly chosen = signal<Record<number, string>>({});
  /** fee type id -> ticked. Only optional fee heads are ever in here; every other head always applies. Optional heads start unticked (opt-in). */
  protected readonly included = signal<Record<number, boolean>>({});

  protected readonly ready = computed(() => !!this.academicYearId() && !!this.classId());

  protected readonly rows = computed(() => {
    const p = this.preview();
    if (!p) return [];
    return p.lines.map((l) => {
      const plan = this.chosen()[l.feeTypeId] ?? 'NONE';
      const option: InstallmentPlanOption | undefined = l.allowedPlans.find((o) => o.code === plan) ?? l.allowedPlans[0];
      const installments = option?.installments ?? 1;
      const remaining = option?.remainingInstallments ?? installments;
      const payable = option?.remainingAmount ?? l.amount * l.periodsPerYear;
      const applies = !l.isOptional || this.included()[l.feeTypeId] === true;
      return {
        line: l,
        plan: option?.code ?? 'NONE',
        locked: l.allowedPlans.length <= 1,
        applies,
        installments,
        remaining,
        perInstallment: remaining > 0 ? payable / remaining : 0,
        payable,
        backdatedCount: option?.backdatedInstallments ?? 0,
        backdatedAmount: option?.backdatedAmount ?? 0,
      };
    });
  });

  protected readonly yearTotal = computed(() => this.rows().filter((r) => r.applies).reduce((sum, r) => sum + r.payable, 0));
  protected readonly hasOptional = computed(() => this.rows().some((r) => r.line.isOptional));
  protected readonly chargeFromLabel = computed(() => (this.preview() ? formatDate(this.preview()!.chargeFrom) : ''));
  protected readonly backdated = computed<BackdatingSummary>(() => {
    const p = this.preview();
    const rows = this.rows().filter((r) => r.applies);
    return {
      isBackdated: !!p?.isBackdated,
      allowed: p?.backdatingAllowed ?? true,
      message: p?.backdatingMessage ?? null,
      count: p?.isBackdated ? rows.reduce((s, r) => s + r.backdatedCount, 0) : 0,
      amount: p?.isBackdated ? rows.reduce((s, r) => s + r.backdatedAmount, 0) : 0,
    };
  });

  protected readonly inr = formatInr;

  constructor() {
    // Reload whenever year, class or the charge-from date changes
    effect(() => {
      const year = this.academicYearId();
      const cls = this.classId();
      const start = this.startDate();
      untracked(() => this.load(year, cls, start));
    });
    effect(() => this.backdatingChange.emit(this.backdated()));
  }

  private load(year: number | null, cls: number | null, start: string | null): void {
    // keep what the user already picked while only the date changes
    const sameFees = this.preview()?.academicYearId === year && this.preview()?.classId === cls;
    if (!sameFees) {
      this.preview.set(null);
      this.chosen.set({});
      this.included.set({});
    }
    this.failed.set(false);
    if (!year || !cls) {
      this.selectionsChange.emit([]);
      return;
    }
    this.loading.set(true);
    this.students
      .previewFees(year, cls, start)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => {
          // ignore an answer for a year / class the user has already moved away from
          if (p.academicYearId !== this.academicYearId() || p.classId !== this.classId()) return;
          this.preview.set(p);
          this.emit();
        },
        error: () => {
          this.failed.set(true); // already toasted by the interceptor; the student can still be saved with the defaults
          this.selectionsChange.emit([]);
        },
      });
  }

  protected onInclude(line: FeePreviewLine, event: Event): void {
    this.included.update((c) => ({ ...c, [line.feeTypeId]: (event.target as HTMLInputElement).checked }));
    this.emit();
  }

  protected onPlan(line: FeePreviewLine, event: Event): void {
    const plan = (event.target as HTMLSelectElement).value;
    this.chosen.update((c) => ({ ...c, [line.feeTypeId]: plan }));
    this.emit();
  }

  private emit(): void {
    this.selectionsChange.emit(this.rows().map((r) => ({ feeTypeId: r.line.feeTypeId, installmentPlan: r.plan, isApplicable: r.applies })));
  }
}
