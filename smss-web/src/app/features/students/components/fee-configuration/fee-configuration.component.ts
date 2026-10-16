import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { CardComponent, IconComponent } from '../../../../shared/components';
import { FeeItemSelection, FeePreview, FeePreviewLine } from '../../models/student-billing.model';
import { StudentService } from '../../services/student.service';

/**
 * "Fee Configuration" for the admission forms. Once an academic year and class are chosen it shows the fee heads of that
 * class's fee structure with the frequency each will be paid at (defaulted from the structure, changeable to one that makes
 * sense for that fee head), the amount of each installment and the year total. Nothing is saved here: the choices are emitted
 * and sent with the admission request.
 */
@Component({
  selector: 'app-fee-configuration',
  imports: [NgTemplateOutlet, CardComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './fee-configuration.component.scss',
  templateUrl: './fee-configuration.component.html',
})
export class FeeConfigurationComponent {
  private readonly students = inject(StudentService);
  private readonly destroyRef = inject(DestroyRef);

  readonly academicYearId = input<number | null>(null);
  readonly classId = input<number | null>(null);
  /** The date the student starts (admission date, yyyy-MM-dd): fees are owed from that month, so a mid-year start pays fewer installments. */
  readonly startDate = input<string | null>(null);
  /** Wrap in a card (the onboarding form) or render bare (inside a dialog). */
  readonly framed = input(true);

  /** The choices, one per fee head, whenever they change (and when the structure first loads). */
  readonly selectionsChange = output<FeeItemSelection[]>();

  protected readonly preview = signal<FeePreview | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  /** fee type id -> chosen frequency id */
  protected readonly chosen = signal<Record<number, number>>({});
  /** fee type id -> ticked. Only optional fee heads are ever in here; every other head always applies. Optional heads start unticked (opt-in). */
  protected readonly included = signal<Record<number, boolean>>({});

  protected readonly ready = computed(() => !!this.academicYearId() && !!this.classId());

  protected readonly rows = computed(() => {
    const p = this.preview();
    if (!p) return [];
    return p.lines.map((l) => {
      const frequencyId = this.chosen()[l.feeTypeId] ?? l.structureFrequencyId;
      const option = l.allowedFrequencies.find((f) => f.frequencyId === frequencyId) ?? l.allowedFrequencies[0];
      const yearly = l.amount * l.periodsPerYear;
      const installments = option?.installments ?? 1;
      const remaining = option?.remainingInstallments ?? installments;
      const applies = !l.isOptional || this.included()[l.feeTypeId] === true;
      const perInstallment = yearly / installments;
      return {
        line: l,
        frequencyId,
        locked: l.allowedFrequencies.length <= 1,
        applies,
        installments,
        remaining,
        perInstallment,
        // what is owed this year: a mid-year start owes only the installments from its month (a single charge is always whole)
        payable: installments === 1 ? yearly : perInstallment * remaining,
      };
    });
  });

  protected readonly yearTotal = computed(() => this.rows().filter((r) => r.applies).reduce((sum, r) => sum + r.payable, 0));

  protected readonly hasOptional = computed(() => this.rows().some((r) => r.line.isOptional));
  protected readonly isPartYear = computed(() => this.rows().some((r) => r.installments > 1 && r.remaining < r.installments));

  protected readonly inr = formatInr;

  constructor() {
    // Reload whenever year or class changes
    effect(() => {
      const year = this.academicYearId();
      const cls = this.classId();
      const start = this.startDate();
      untracked(() => this.load(year, cls, start));
    });
  }

  private load(year: number | null, cls: number | null, start: string | null): void {
    // keep what the user already picked while only the start date changes
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

  protected onFrequency(line: FeePreviewLine, event: Event): void {
    const frequencyId = Number((event.target as HTMLSelectElement).value);
    this.chosen.update((c) => ({ ...c, [line.feeTypeId]: frequencyId }));
    this.emit();
  }

  private emit(): void {
    this.selectionsChange.emit(this.rows().map((r) => ({ feeTypeId: r.line.feeTypeId, frequencyId: r.frequencyId, isApplicable: r.applies })));
  }
}
