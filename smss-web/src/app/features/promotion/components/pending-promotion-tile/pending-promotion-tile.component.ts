import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { PromotionPending } from '../../models/promotion.model';
import { PromotionService } from '../../services/promotion.service';

type TileState = { kind: 'loading' } | { kind: 'hidden' } | { kind: 'unavailable' } | { kind: 'count'; pending: PromotionPending };

/**
 * "Pending promotion" tile for the School Admin and Principal dashboards: how many students were enrolled in the year before the current year and have no enrolment in the current one. It asks for itself
 * (so the dashboard it sits on is unaffected), is hidden when the school has no earlier year, and shows "Unavailable" (never 0) when the count cannot be read.
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
          <small>{{ count() === 0 ? 'Everyone from ' + earlier() + ' is in ' + current() : (count() === 1 ? 'student' : 'students') + ' from ' + earlier() + ' not yet in ' + current() }}</small>
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
  protected earlier(): string { const s = this.state(); return s.kind === 'count' ? (s.pending.earlierYearName ?? '') : ''; }
  protected current(): string { const s = this.state(); return s.kind === 'count' ? (s.pending.currentYearName ?? '') : ''; }

  ngOnInit(): void {
    this.api.pending(true).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (p) => this.state.set(p.hasEarlierYear ? { kind: 'count', pending: p } : { kind: 'hidden' }),
      error: () => this.state.set({ kind: 'unavailable' }),
    });
  }
}
