import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ButtonComponent, CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { PromotionPending } from '../../models/promotion.model';
import { PromotionService } from '../../services/promotion.service';

/** The students who were enrolled in the year before the current year and are not yet enrolled in it, with BLOCKED (unpaid dues up to today) or "no decision yet". Read-only; the run is on the Promotion page. */
@Component({
  selector: 'app-pending-promotion',
  imports: [CurrencyPipe, RouterLink, ButtonComponent, CardComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="Pending promotion" subtitle="Students waiting to be promoted into the year named below" icon="graduation-cap" [breadcrumbs]="[{ label: 'Promotion', link: '/promotion' }, { label: 'Pending' }]">
      <a routerLink="/promotion"><app-button>Go to Promotion</app-button></a>
    </app-page-header>
    <app-card>
      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (failed()) {
        <p role="alert" data-failed>The list could not be loaded. Please try again.</p>
      } @else if (!data()!.hasPair) {
        <p data-no-earlier>This school has no next year to promote into and no earlier year to promote from, so nothing is waiting for promotion.</p>
      } @else if (data()!.total === 0) {
        <p data-none data-pair>Showing {{ data()!.sourceYearName }} into {{ data()!.targetYearName }}. Everyone enrolled in {{ data()!.sourceYearName }} is already in {{ data()!.targetYearName }} (or has passed out or left).</p>
      } @else {
        <p class="sum" data-summary data-pair>Showing {{ data()!.sourceYearName }} into {{ data()!.targetYearName }}{{ data()!.targetIsCurrent ? ' (the current year)' : ' (the next year, being prepared)' }}: {{ data()!.total }} student{{ data()!.total === 1 ? '' : 's' }} not yet promoted; {{ data()!.blocked }} blocked by dues.</p>
        <div class="scroll">
          <table class="rows" data-rows>
            <thead><tr><th>Student</th><th>Class</th><th>Teacher decision</th><th>Dues up to today</th><th>Status</th></tr></thead>
            <tbody>
              @for (r of data()!.rows; track r.studentId) {
                <tr [attr.data-row]="r.studentId">
                  <td>{{ r.studentName }}<small>{{ r.admissionNumber }}</small></td>
                  <td>{{ r.className }} {{ r.sectionName }}<small>Roll {{ r.rollNumber ?? '-' }}</small></td>
                  <td>{{ r.teacherDecision ?? '-' }}</td>
                  <td>{{ r.outstanding > 0 ? (r.outstanding | currency: 'INR' : 'symbol' : '1.0-0') : '-' }}</td>
                  <td>{{ r.status === 'BLOCKED' ? 'Blocked: dues' : 'No decision yet' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </app-card>
  `,
  styles: `
    .scroll { overflow-x: auto; }
    .rows { width: 100%; border-collapse: collapse; font-size: var(--text-sm); }
    .rows th, .rows td { text-align: left; padding: var(--space-2) var(--space-3); border-bottom: 1px solid var(--color-border); vertical-align: top; }
    small { display: block; color: var(--color-text-muted); }
    .sum { margin: 0 0 var(--space-3); }
  `,
})
export class PendingPromotionComponent implements OnInit {
  private readonly api = inject(PromotionService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly data = signal<PromotionPending | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  ngOnInit(): void {
    this.api.pending().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (d) => this.data.set(d), error: () => this.failed.set(true) });
  }
}
