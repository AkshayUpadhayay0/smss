import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { BackdatingPreview } from '../../models/fee-structure.model';
import { FeeStructureService } from '../../services/fee-structure.service';

/**
 * "Apply to earlier months" for one fee line of a saved structure: pick the date to apply from, see exactly what would be created for each student (nothing is written yet), give a
 * reason, tick the confirmation, apply. The server repeats every check; it also refuses when what it would create no longer matches the preview.
 */
@Component({
  selector: 'app-backdating-dialog',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ModalComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './backdating-dialog.component.scss',
  templateUrl: './backdating-dialog.component.html',
})
export class BackdatingDialogComponent {
  private readonly service = inject(FeeStructureService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly feeStructureId = input.required<number>();
  readonly feeTypeId = input.required<number>();
  readonly feeTypeName = input.required<string>();
  /** The line's current effective date (the new date must be earlier). yyyy-MM-dd. */
  readonly currentEffectiveFrom = input.required<string>();
  /** The first day of the academic year (the earliest date allowed). yyyy-MM-dd. */
  readonly yearStart = input.required<string>();

  readonly closed = output<void>();
  /** Emitted after a successful apply, so the builder reloads the line (its effective date has moved back). */
  readonly applied = output<void>();

  protected readonly fromDate = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  protected readonly reason = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500)] });
  protected readonly confirmed = new FormControl(false, { nonNullable: true });

  protected readonly preview = signal<BackdatingPreview | null>(null);
  protected readonly working = signal(false);
  protected readonly dateError = signal<string | undefined>(undefined);
  protected readonly reasonError = signal<string | undefined>(undefined);

  protected readonly inr = formatInr;
  protected readonly fmt = formatDate;
  protected readonly canApply = computed(() => !!this.preview() && this.preview()!.duesCount > 0);

  constructor() {
    queueMicrotask(() => this.fromDate.setValue(this.yearStart()));
    // a changed date makes the preview stale: it has to be looked at again
    this.fromDate.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.preview.set(null);
      this.confirmed.setValue(false);
      this.dateError.set(undefined);
    });
  }

  protected showPreview(): void {
    const from = this.fromDate.value;
    if (!from) {
      this.dateError.set('Choose the date to apply from');
      return;
    }
    if (from >= this.currentEffectiveFrom()) {
      this.dateError.set(`Choose a date before ${formatDate(this.currentEffectiveFrom())}, where this fee currently starts`);
      return;
    }
    if (from < this.yearStart()) {
      this.dateError.set('The date cannot be before the start of the academic year');
      return;
    }
    if (this.working()) return;
    this.working.set(true);
    this.service
      .backdatingPreview(this.feeStructureId(), this.feeTypeId(), from)
      .pipe(finalize(() => this.working.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (p) => this.preview.set(p), error: () => undefined }); // the server's reason is toasted
  }

  protected apply(): void {
    const p = this.preview();
    if (!p || this.working()) return;
    if (!this.reason.value.trim()) {
      this.reason.markAsTouched();
      this.reasonError.set('A reason is required');
      return;
    }
    this.reasonError.set(undefined);
    if (!this.confirmed.value) return;
    this.working.set(true);
    this.service
      .backdatingApply(this.feeStructureId(), this.feeTypeId(), { fromDate: p.fromDate, reason: this.reason.value.trim(), expectedDuesCount: p.duesCount })
      .pipe(finalize(() => this.working.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.toast.success(`${r.duesCount} due(s) worth ${formatInr(r.totalAmount)} created for ${r.studentsCount} student(s).`);
          this.applied.emit();
          this.closed.emit();
        },
        error: () => undefined, // e.g. 409 "no longer matches the preview": the dialog stays open and the preview can be re-run
      });
  }
}
