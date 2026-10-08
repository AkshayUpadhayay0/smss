import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'app-pagination',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './pagination.component.scss',
  template: `
    <div class="bar">
      <span class="summary">Showing {{ from() }}–{{ to() }} of {{ totalItems() }}</span>

      <div class="controls">
        <label class="size">
          <span>Rows per page</span>
          <select [value]="pageSize()" (change)="onSizeChange($event)">
            @for (s of pageSizeOptions(); track s) {
              <option [value]="s" [selected]="s === pageSize()">{{ s }}</option>
            }
          </select>
        </label>

        <nav class="pages" aria-label="Pagination">
          <button type="button" class="nav" [disabled]="currentPage() <= 1" (click)="go(currentPage() - 1)">
            <app-icon name="chevron-left" [size]="14" />Previous
          </button>
          @for (p of pages(); track $index) {
            @if (p === null) {
              <span class="gap">…</span>
            } @else {
              <button type="button" class="page" [class.current]="p === currentPage()" (click)="go(p)">{{ p }}</button>
            }
          }
          <button type="button" class="nav" [disabled]="currentPage() >= totalPages()" (click)="go(currentPage() + 1)">
            Next<app-icon name="chevron-right" [size]="14" />
          </button>
        </nav>
      </div>
    </div>
  `,
})
export class PaginationComponent {
  readonly currentPage = input(1);
  readonly totalItems = input(0);
  readonly pageSize = input(10);
  readonly pageSizeOptions = input([10, 25, 50, 100]);

  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalItems() / this.pageSize())));
  protected readonly from = computed(() => (this.totalItems() === 0 ? 0 : (this.currentPage() - 1) * this.pageSize() + 1));
  protected readonly to = computed(() => Math.min(this.currentPage() * this.pageSize(), this.totalItems()));

  /** Page numbers with `null` marking an ellipsis gap. */
  protected readonly pages = computed<(number | null)[]>(() => {
    const total = this.totalPages();
    const current = this.currentPage();
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const set = new Set([1, total, current, current - 1, current + 1].filter((p) => p >= 1 && p <= total));
    const sorted = [...set].sort((a, b) => a - b);
    const out: (number | null)[] = [];
    sorted.forEach((p, i) => {
      if (i > 0 && p - sorted[i - 1] > 1) out.push(null);
      out.push(p);
    });
    return out;
  });

  protected go(page: number): void {
    if (page >= 1 && page <= this.totalPages() && page !== this.currentPage()) {
      this.pageChange.emit(page);
    }
  }

  protected onSizeChange(event: Event): void {
    this.pageSizeChange.emit(Number((event.target as HTMLSelectElement).value));
  }
}
