import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { AcademicYear } from '../../models/academic-year.model';
import { FeeRepairPreview, FeeRepairResult } from '../../models/fee-repair.model';
import { FeeRepairService } from '../../services/fee-repair.service';

/**
 * "Assign fees for a year" (School Admin): enrolled students with no fee structure get their class's active structure, the same way admission does it. A student who already has fees is never
 * touched, so running it again does nothing. Dues start from today (nothing is back-dated here). One run takes up to the limit the server states; run again for the rest.
 */
@Component({
  selector: 'app-fee-repair-modal',
  imports: [ButtonComponent, IconComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="'Assign fees for ' + year().yearName" [dismissible]="!busy()" (closed)="closed.emit()">
      <div class="body" data-fee-repair>
        @if (loading()) {
          <p role="status">Checking who has no fees…</p>
        } @else if (loadFailed()) {
          <p role="alert">This could not be checked. Please try again.</p>
        } @else if (result(); as r) {
          <p class="verdict ok" data-done>
            <app-icon name="circle-check" [size]="16" />
            {{ doneText(r) }}
          </p>
          @if (r.skippedNoStructure > 0) { <p class="note">{{ r.skippedNoStructure }} student{{ r.skippedNoStructure === 1 ? ' was' : 's were' }} left out because their class has no fee structure.</p> }
          @if (r.remaining > 0) { <p class="note" data-remaining>{{ r.remaining }} more student{{ r.remaining === 1 ? '' : 's' }} still need fees: run this again.</p> }
          @if (r.failed.length > 0) {
            <p class="warn" role="alert" data-failed-title>Fees could not be assigned to {{ r.failed.length }} student{{ r.failed.length === 1 ? '' : 's' }}:</p>
            <ul data-failed>
              @for (f of r.failed; track f.studentId) { <li>{{ f.studentName }} ({{ f.studentId }}, {{ f.className }}): {{ f.reason }}</li> }
            </ul>
          }
        } @else if (preview(); as p) {
          @if (p.missingCount === 0) {
            <p class="verdict ok" data-none><app-icon name="circle-check" [size]="16" />Every enrolled student already has fees for {{ p.yearName }}.</p>
          } @else {
            <p class="verdict" data-count><app-icon name="circle-alert" [size]="16" /><strong>{{ p.missingCount }}</strong> enrolled student{{ p.missingCount === 1 ? ' has' : 's have' }} no fees for {{ p.yearName }}. Fees would be assigned to <strong>{{ p.wouldAssign }}</strong>.</p>
            @if (p.wouldAssign === p.maxPerRun) { <p class="note">One run takes up to {{ p.maxPerRun }} students; run it again for the rest.</p> }
            @if (p.classesWithoutStructure.length > 0) {
              <p class="note">These classes have no active fee structure for {{ p.yearName }}, so their students are left out (add the structure on the Fee Structure screen first):</p>
              <ul data-no-structure>
                @for (c of p.classesWithoutStructure; track c.className) { <li>{{ c.className }}: {{ c.students }} student{{ c.students === 1 ? '' : 's' }}</li> }
              </ul>
            }
            <p class="note">Optional fees chosen last year carry over.</p>
            @if (p.yearStarted && p.backdatingAllowed) {
              <label class="check"><input type="checkbox" data-from-start [checked]="fromStart()" (change)="fromStart.set($any($event.target).checked)" />
                <span>Charge from the year's start ({{ p.yearStart }}) <small>The year has already begun, so this creates dues that are already past. Without this, fees are charged from today and nothing is back-dated.</small></span></label>
              @if (fromStart()) {
                <label class="check"><input type="checkbox" data-confirm-backdating [checked]="confirmed()" (change)="confirmed.set($any($event.target).checked)" />
                  <span>I confirm: dues dated before today will be created and logged.</span></label>
                <label class="field"><span>Reason (required)</span>
                  <textarea data-reason rows="2" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea></label>
              }
            } @else if (p.yearStarted) {
              <p class="note" data-no-backdating>Fees are charged from today. Charging from the year's start is only possible in the current academic year.</p>
            } @else {
              <p class="note">Fees are charged from the year's start.</p>
            }
          }
        }
      </div>
      <ng-container modal-footer>
        @if (result()) {
          <app-button (click)="closed.emit()">Close</app-button>
        } @else {
          <app-button variant="secondary" [disabled]="busy()" (click)="closed.emit()">Not now</app-button>
          <app-button data-assign-btn [disabled]="busy() || loading() || !preview() || preview()!.wouldAssign === 0 || !canRun()" (click)="run()">{{ busy() ? 'Assigning…' : 'Assign fees' }}</app-button>
        }
      </ng-container>
    </app-modal>
  `,
  styles: `
    .body { display: flex; flex-direction: column; gap: var(--space-3); min-width: min(520px, 84vw); }
    .verdict { display: flex; gap: var(--space-2); align-items: flex-start; margin: 0; }
    .verdict.ok { color: var(--color-success, #15803d); }
    .note { margin: 0; font-size: var(--text-sm); color: var(--color-text-muted); }
    .warn { margin: 0; color: var(--color-danger, #b91c1c); font-weight: var(--weight-semibold); }
    .check { display: flex; gap: var(--space-2); align-items: flex-start; font-size: var(--text-sm); small { display: block; color: var(--color-text-muted); } }
    .field { display: flex; flex-direction: column; gap: var(--space-1); font-size: var(--text-sm); }
    ul { margin: 0; padding-left: var(--space-5); font-size: var(--text-sm); }
  `,
})
export class FeeRepairModalComponent implements OnInit {
  private readonly api = inject(FeeRepairService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly year = input.required<AcademicYear>();
  readonly closed = output<void>();
  /** A run happened (the screens behind may have changed). */
  readonly done = output<FeeRepairResult>();

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly preview = signal<FeeRepairPreview | null>(null);
  protected readonly result = signal<FeeRepairResult | null>(null);
  protected readonly fromStart = signal(false);
  protected readonly confirmed = signal(false);
  protected readonly reason = signal('');
  /** Charging from the year's start needs the confirmation and a written reason. */
  protected readonly canRun = computed(() => !this.fromStart() || (this.confirmed() && this.reason().trim().length > 0));

  ngOnInit(): void {
    this.api
      .missing(this.year().academicYearId)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (p) => this.preview.set(p), error: () => this.loadFailed.set(true) });
  }

  protected doneText(r: FeeRepairResult): string {
    if (r.assigned === 0 && r.failed.length === 0) return 'Nobody needed fees assigned.';
    return `Fees assigned to ${r.assigned} student${r.assigned === 1 ? '' : 's'}${r.backdated ? ", charged from the year's start" : ''}.`;
  }

  protected run(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api
      .assign(this.year().academicYearId, { chargeFromYearStart: this.fromStart(), confirmBackdating: this.fromStart() && this.confirmed(), reason: this.fromStart() ? this.reason().trim() : null })
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.result.set(r);
          this.toast.success(r.failed.length === 0 ? 'Fees assigned.' : 'Fees assigned, but some students need attention.');
          this.done.emit(r);
        },
        error: () => undefined,   // the server's reason is toasted by the interceptor
      });
  }
}
