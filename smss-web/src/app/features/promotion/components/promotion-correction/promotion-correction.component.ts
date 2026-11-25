import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent } from '../../../../shared/components';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { PromotionCorrectionCheck, PromotionCorrectionResult } from '../../models/promotion.model';
import { PromotionService } from '../../services/promotion.service';

/**
 * "Correct a promotion" (School Admin and Principal): for ONE student, undo a promotion that was done wrongly. It removes the student's new-year enrolment and its unpaid fees, clears the outcome of the old
 * enrolment and records who did it and why; the student can then be promoted again from the normal run. It is refused, in plain words, once the new year holds attendance, a payment, marks, transport or a
 * concession. The server decides everything: this screen only asks and shows the answer.
 */
@Component({
  selector: 'app-promotion-correction',
  imports: [CurrencyPipe, ButtonComponent, CardComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-card title="Correct a promotion">
      <p class="hint">A student was promoted (or passed out, or left) by mistake? Enter their student id and the year they were promoted from. This is only possible while nothing has been recorded for them in the new year.</p>
      <div class="filters">
        <label class="field"><span>Student id</span><input data-student [value]="studentId()" (input)="studentId.set($any($event.target).value); clear()" autocomplete="off" /></label>
        <label class="field"><span>Promoted from</span>
          <select data-year (change)="yearId.set(+$any($event.target).value); clear()">
            @for (y of years(); track y.academicYearId) { <option [value]="y.academicYearId" [selected]="y.academicYearId === yearId()">{{ y.yearName }}</option> }
          </select></label>
        <app-button data-check-btn variant="secondary" [disabled]="!studentId().trim() || busy()" (click)="check()">Check</app-button>
      </div>
      @if (failed()) { <p role="alert" class="issue" data-failed>That could not be checked. Please try again.</p> }
      @if (done(); as d) {
        <p class="verdict ok" data-done><app-icon name="circle-check" [size]="16" />Corrected. {{ d.studentId }} can be promoted again from the run above.</p>
      }
      @if (result(); as c) {
        <div data-check>
          @if (c.canCorrect) {
            <p>{{ c.studentName }} ({{ c.studentId }}): the outcome <strong>{{ c.outcome }}</strong> in {{ c.sourceYearName }} will be cleared.
              @if (c.willRemove.enrolment) {
                The enrolment in {{ c.willRemove.newYearName }} ({{ c.willRemove.className }} {{ c.willRemove.sectionName }}, roll {{ c.willRemove.rollNumber ?? '-' }}) is removed together with
                {{ c.willRemove.feeItems }} fee item{{ c.willRemove.feeItems === 1 ? '' : 's' }} and {{ c.willRemove.dues }} due{{ c.willRemove.dues === 1 ? '' : 's' }} ({{ c.willRemove.duesAmount | currency: 'INR' : 'symbol' : '1.0-0' }}).
              } @else { There is no new-year enrolment to remove. }
            </p>
            <label class="field wide"><span>Reason (at least 10 characters)</span><textarea data-reason rows="2" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea></label>
            <app-button data-correct-btn [disabled]="reason().trim().length < 10 || busy()" (click)="correct(c)">{{ busy() ? 'Correcting…' : 'Correct this promotion' }}</app-button>
          } @else {
            <p class="issue" role="alert" data-blocked>This promotion cannot be corrected:</p>
            <ul data-blockers>@for (b of c.blockers; track b) { <li>{{ b }}</li> }</ul>
          }
        </div>
      }
    </app-card>
  `,
  styles: `
    .filters { display: flex; flex-wrap: wrap; gap: var(--space-4); align-items: flex-end; }
    .field { display: flex; flex-direction: column; gap: var(--space-1); font-size: var(--text-sm); min-width: 180px; }
    .wide { min-width: 100%; textarea { width: 100%; } }
    .hint { margin: 0 0 var(--space-3); font-size: var(--text-sm); color: var(--color-text-muted); }
    .issue { color: #991b1b; margin: var(--space-3) 0 0; }
    .verdict { display: flex; gap: var(--space-2); align-items: center; }
    .ok { color: #166534; }
  `,
})
export class PromotionCorrectionComponent {
  private readonly api = inject(PromotionService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly years = input.required<AcademicYear[]>();
  readonly defaultYearId = input<number | null>(null);

  protected readonly studentId = signal('');
  private readonly pickedYear = signal<number | null>(null);
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly failed = signal(false);
  protected readonly result = signal<PromotionCorrectionCheck | null>(null);
  protected readonly done = signal<PromotionCorrectionResult | null>(null);

  protected readonly yearId = Object.assign(
    () => this.pickedYear() ?? this.defaultYearId() ?? this.years()[0]?.academicYearId ?? 0,
    { set: (v: number) => this.pickedYear.set(v) },
  );

  protected clear(): void {
    this.result.set(null);
    this.done.set(null);
    this.failed.set(false);
  }

  protected check(): void {
    if (!this.studentId().trim() || this.busy()) return;
    this.busy.set(true);
    this.clear();
    this.api
      .checkCorrection(this.studentId().trim(), this.yearId())
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (c) => this.result.set(c), error: () => this.failed.set(true) });
  }

  protected async correct(c: PromotionCorrectionCheck): Promise<void> {
    if (this.reason().trim().length < 10 || this.busy()) return;
    const ok = await this.confirmDialog.confirm({
      title: 'Correct this promotion?',
      message: `The promotion of ${c.studentName} out of ${c.sourceYearName} will be undone and recorded in the audit.`,
      confirmText: 'Correct it',
      variant: 'danger',
    });
    if (!ok) return;
    this.busy.set(true);
    this.api
      .correct(c.studentId, this.yearId(), this.reason().trim())
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.result.set(null);
          this.reason.set('');
          this.done.set(r);
          this.toast.success('The promotion was corrected.');
        },
        error: () => undefined,   // the server's plain reason (what blocks it) is toasted; nothing was changed
      });
  }
}
