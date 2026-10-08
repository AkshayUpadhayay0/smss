import { ChangeDetectionStrategy, Component, ContentChild, TemplateRef, computed, contentChildren, input, output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { IconComponent } from '../icon/icon.component';
import { SkeletonComponent } from '../skeleton/skeleton.component';
import { EmptyStateComponent } from '../empty-state/empty-state.component';
import { SortDirection, TableColumn } from '../../../core/models';
import { TableCellContext, TableCellDirective } from './table-cell.directive';

/**
 * Generic, reusable data-table shell. Consumers pass `columns` + `rows`
 * (any shape) and optionally project a per-row actions template via
 * `<ng-template #rowActions let-row>...</ng-template>` — the table never
 * needs to know what actions a particular page wants to show.
 * Individual columns can be custom-rendered with
 * `<ng-template appTableCell="columnKey" let-row>...</ng-template>`.
 */
@Component({
  selector: 'app-table',
  standalone: true,
  imports: [NgTemplateOutlet, IconComponent, SkeletonComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './table.component.html',
  styleUrl: './table.component.scss',
})
export class TableComponent<T extends object> {
  readonly columns = input.required<TableColumn<T>[]>();
  readonly rows = input.required<T[]>();
  readonly loading = input<boolean>(false);
  readonly sortKey = input<string | null>(null);
  readonly sortDirection = input<SortDirection>(null);
  readonly hasActions = input<boolean>(false);
  readonly emptyMessage = input<string>('No records found.');
  readonly emptyTitle = input<string>('No data');

  readonly sortChange = output<string>();

  @ContentChild('rowActions') rowActionsTemplate?: TemplateRef<{ $implicit: T }>;

  private readonly cellDirectives = contentChildren(TableCellDirective);

  /** Column key -> custom cell template. */
  readonly cellTemplates = computed(() => {
    const map = new Map<string, TemplateRef<TableCellContext<T>>>();
    for (const dir of this.cellDirectives()) map.set(dir.appTableCell(), dir.template);
    return map;
  });

  readonly skeletonRows = Array.from({ length: 5 });

  onHeaderClick(column: TableColumn<T>): void {
    if (!column.sortable) return;
    this.sortChange.emit(column.key);
  }

  sortIconFor(column: TableColumn<T>): string {
    if (this.sortKey() !== column.key) return 'arrow-up-down';
    return this.sortDirection() === 'asc' ? 'arrow-up' : 'arrow-down';
  }
}
