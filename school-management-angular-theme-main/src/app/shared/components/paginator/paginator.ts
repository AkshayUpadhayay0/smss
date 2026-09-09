import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { IconComponent } from '../icon/icon';

@Component({
  selector: 'app-paginator',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="paginator">
      <span class="range text-secondary text-sm">
        Showing {{ startIndex() }}–{{ endIndex() }} of {{ total() }}
      </span>
      <div class="controls">
        <button class="btn btn-outline btn-icon btn-sm" [disabled]="page() <= 1" (click)="goTo(page() - 1)" aria-label="Previous page">
          <app-icon name="chevron-left" [size]="16" />
        </button>
        @for (p of pages(); track p) {
          @if (p === -1) {
            <span class="ellipsis">…</span>
          } @else {
            <button class="page-btn" [class.active]="p === page()" (click)="goTo(p)">{{ p }}</button>
          }
        }
        <button class="btn btn-outline btn-icon btn-sm" [disabled]="page() >= totalPages()" (click)="goTo(page() + 1)" aria-label="Next page">
          <app-icon name="chevron-right" [size]="16" />
        </button>
      </div>
    </div>
  `,
  styles: [`
    .paginator { display: flex; align-items: center; justify-content: space-between; gap: var(--space-4); flex-wrap: wrap; padding-top: var(--space-4); }
    .controls { display: flex; align-items: center; gap: 6px; }
    .page-btn {
      min-width: 32px; height: 32px; border-radius: var(--radius-md); border: 1px solid transparent;
      background: transparent; color: var(--text-secondary); font-size: 12.5px; font-weight: 600; cursor: pointer;
    }
    .page-btn:hover { background: var(--surface-alt); }
    .page-btn.active { background: var(--primary); color: #fff; }
    .ellipsis { color: var(--text-muted); padding: 0 2px; }
    @media (max-width: 560px) { .paginator { justify-content: center; } .range { display: none; } }
  `],
})
export class PaginatorComponent {
  total = input.required<number>();
  pageSize = input<number>(10);
  page = input<number>(1);
  pageChange = output<number>();

  totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  startIndex = computed(() => this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1);
  endIndex = computed(() => Math.min(this.total(), this.page() * this.pageSize()));

  pages = computed<number[]>(() => {
    const total = this.totalPages();
    const cur = this.page();
    const result: number[] = [];
    const add = (n: number) => { if (!result.includes(n)) result.push(n); };
    add(1);
    for (let i = cur - 1; i <= cur + 1; i++) if (i > 1 && i < total) add(i);
    if (total > 1) add(total);
    const withGaps: number[] = [];
    let prev = 0;
    for (const p of result.sort((a, b) => a - b)) {
      if (prev && p - prev > 1) withGaps.push(-1);
      withGaps.push(p);
      prev = p;
    }
    return withGaps;
  });

  goTo(p: number): void {
    if (p >= 1 && p <= this.totalPages()) this.pageChange.emit(p);
  }
}
