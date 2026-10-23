import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { TimetableGridComponent } from '../../components/timetable-grid/timetable-grid.component';
import { TimetableGrid } from '../../models/timetable.model';
import { TimetableService } from '../../services/timetable.service';

/** A teacher's own weekly timetable (current academic year), read-only. */
@Component({
  selector: 'app-my-timetable',
  imports: [CardComponent, PageHeaderComponent, TimetableGridComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="My Timetable" subtitle="Your classes for the week" icon="calendar" [breadcrumbs]="breadcrumbs" />

    <app-card>
      @if (loading()) {
        <p class="note" role="status">Loading timetable…</p>
      } @else if (grid(); as g) {
        @if (g.rows.length === 0) {
          <p class="note">No periods have been set up yet.</p>
        } @else {
          <p class="note summary">{{ g.academicYearName }} · {{ g.entryCount }} {{ g.entryCount === 1 ? 'period' : 'periods' }} a week</p>
          <app-timetable-grid [grid]="g" />
        }
      } @else {
        <p class="note" role="alert">Your timetable could not be loaded.</p>
      }
    </app-card>
  `,
  styles: `
    .note {
      margin: 0;
      color: var(--color-text-muted);
    }

    .summary {
      margin-bottom: var(--space-4);
      font-size: var(--text-sm);
    }
  `,
})
export class MyTimetableComponent implements OnInit {
  private readonly service = inject(TimetableService);

  protected readonly breadcrumbs = [{ label: 'My Timetable' }];
  protected readonly grid = signal<TimetableGrid | null>(null);
  protected readonly loading = signal(true);

  ngOnInit(): void {
    this.service
      .myGrid()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (g) => this.grid.set(g),
        error: () => this.grid.set(null), // already toasted by the interceptor
      });
  }
}
