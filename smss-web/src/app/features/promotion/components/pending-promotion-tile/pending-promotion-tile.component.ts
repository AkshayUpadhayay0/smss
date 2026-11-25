import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { PromotionPending } from '../../models/promotion.model';
import { PromotionService } from '../../services/promotion.service';

type TileState = { kind: 'loading' } | { kind: 'hidden' } | { kind: 'unavailable' } | { kind: 'count'; pending: PromotionPending };

/**
 * "Pending promotion" tile for the School Admin and Principal dashboards: how many students are in the source year and have no enrolment in the target year of the pair it names (the next year being prepared when there is one, else the current year and the year before it). It asks for itself
 * (so the dashboard it sits on is unaffected), is hidden when the school has no year pair (no next year to promote into and no earlier year to promote from), and shows "Unavailable" (never 0) when the count cannot be read.
 */
@Component({
  selector: 'app-pending-promotion-tile',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (state().kind) {
      @case ('count') {
        <a class="tile" routerLink="/promotion/pending" data-pending-tile>
          <span class="label">Pending promotion</span>
          <strong data-pending-count>{{ count() }}</strong>
          <small data-pair>{{ count() === 0 ? 'Everyone from ' + source() + ' is in ' + target() : (count() === 1 ? 'student' : 'students') + ' from ' + source() + ' not yet in ' + target() }}</small>
        </a>
      }
      @case ('unavailable') {
        <div class="tile unavailable" data-pending-tile role="status">
          <span class="label">Pending promotion</span>
          <strong data-pending-count>Unavailable</strong>
          <small>The count could not be read. Try again later.</small>
        </div>
      }
    }
  `,
  styles: `
    :host { display: block; }
    .tile { display: flex; flex-direction: column; gap: 2px; padding: var(--space-4); border-radius: var(--radius-lg, 12px); background: var(--color-surface, #fff); border: 1px solid var(--color-border); color: inherit; text-decoration: none; margin-bottom: var(--space-4); }
    .label { font-size: var(--text-sm); color: var(--color-text-muted); }
    strong { font-size: var(--text-2xl, 24px); }
    small { color: var(--color-text-muted); }
    .unavailable strong { color: var(--color-text-muted); font-size: var(--text-lg, 18px); }
  `,
})
export class PendingPromotionTileComponent implements OnInit {
  private readonly api = inject(PromotionService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly state = signal<TileState>({ kind: 'loading' });

  protected count(): number { const s = this.state(); return s.kind === 'count' ? s.pending.total : 0; }
  protected source(): string { const s = this.state(); return s.kind === 'count' ? (s.pending.sourceYearName ?? '') : ''; }
  protected target(): string { const s = this.state(); return s.kind === 'count' ? (s.pending.targetYearName ?? '') : ''; }

  ngOnInit(): void {
    this.api.pending(true).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (p) => this.state.set(p.hasPair ? { kind: 'count', pending: p } : { kind: 'hidden' }),
      error: () => this.state.set({ kind: 'unavailable' }),
    });
  }
}
