import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SectionChangeHistory } from '../../models/student-leaving.model';
import { StudentLeavingService } from '../../services/student-leaving.service';

/** A student's mid-year section changes (from, to, roll numbers, date, reason, who). Shown on the student profile to the School Admin and the Principal only; nothing shows when there is no change. */
@Component({
  selector: 'app-section-history',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rows().length === 0) {
      <p class="none" data-none>No section changes.</p>
    } @else {
      <div data-history>
        <div class="scroll">
          <table class="rows">
            <thead><tr><th>Date</th><th>Year</th><th>From</th><th>To</th><th>Roll</th><th>Reason</th><th>By</th></tr></thead>
            <tbody>
              @for (h of rows(); track h.changeId) {
                <tr data-row>
                  <td>{{ h.effectiveDate | date: 'd MMM yyyy' }}</td><td>{{ h.yearName }}</td><td>{{ h.fromSection }}</td><td>{{ h.toSection }}</td>
                  <td>{{ h.fromRoll ?? '-' }} → {{ h.toRoll ?? '-' }}</td><td>{{ h.reason }}</td><td>{{ h.changedBy ?? '-' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    }
  `,
  styles: `
    .none { margin: 0; color: var(--color-text-muted); font-size: var(--text-sm); }
    .scroll { overflow-x: auto; }
    .rows { width: 100%; border-collapse: collapse; font-size: var(--text-sm); }
    .rows th, .rows td { text-align: left; padding: var(--space-2) var(--space-3); border-bottom: 1px solid var(--color-border); vertical-align: top; }
  `,
})
export class SectionHistoryComponent {
  private readonly api = inject(StudentLeavingService);
  private readonly destroyRef = inject(DestroyRef);
  readonly studentId = input.required<string>();
  /** Bump it to read the history again (after a change was made on the same page). */
  readonly refreshKey = input(0);
  protected readonly rows = signal<SectionChangeHistory[]>([]);

  constructor() {
    effect(() => {
      const id = this.studentId();
      this.refreshKey();
      untracked(() => this.api.sectionHistory(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (r) => this.rows.set(Array.isArray(r) ? r : []), error: () => this.rows.set([]) }));
    });
  }
}
