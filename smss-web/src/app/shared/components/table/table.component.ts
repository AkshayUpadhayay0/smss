import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, TemplateRef, contentChild, input, output } from '@angular/core';
import { IconComponent } from '../icon/icon.component';

export type SortDirection = 'asc' | 'desc';

export interface TableColumn<T> {
  key: keyof T & string;
  label: string;
  sortable?: boolean;
  width?: string;
  /**
   * 'status' renders the value as an Active/Inactive pill (boolean, or a string such as "Active").
   * 'badge' renders the text in the same pill shape, highlighted only when it is "Yes" (e.g. Required: Yes / No).
   */
  type?: 'text' | 'status' | 'badge';
}

export interface SortChange {
  key: string;
  direction: SortDirection;
}

/**
 * Sorting is delegated: the table shows indicators and emits `sortChange`; the parent sorts `rows`.
 * Row actions: `<ng-template #rowActions let-row>…</ng-template>` with `[hasActions]="true"`.
 */
@Component({
  selector: 'app-table',
  imports: [NgTemplateOutlet, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './table.component.scss',
  template: `
    <div class="wrap">
      <table>
        <thead>
          <tr>
            @for (col of columns(); track col.key) {
              <th [style.width]="col.width" [attr.aria-sort]="ariaSort(col.key)">
                @if (col.sortable) {
                  <button type="button" class="sort" (click)="toggleSort(col.key)">
                    {{ col.label }}
                    <app-icon [name]="sortIcon(col.key)" [size]="14" [class.active]="sortKey() === col.key" />
                  </button>
                } @else {
                  {{ col.label }}
                }
              </th>
            }
            @if (hasActions()) {
              <th class="actions-col">Actions</th>
            }
          </tr>
        </thead>
        <tbody>
          @if (loading()) {
            @for (n of skeletonRows; track n) {
              <tr class="skeleton">
                @for (col of columns(); track col.key) {
                  <td><span class="bar"></span></td>
                }
                @if (hasActions()) {
                  <td><span class="bar short"></span></td>
                }
              </tr>
            }
          } @else {
            @for (row of rows(); track $index) {
              <tr>
                @for (col of columns(); track col.key) {
                  <td>
                    @if (col.type === 'status') {
                      <span class="pill" [class.active]="isActive(cell(row, col.key))">
                        {{ statusLabel(cell(row, col.key)) }}
                      </span>
                    } @else if (col.type === 'badge') {
                      <span class="pill" [class.active]="cell(row, col.key) === 'Yes'">{{ cell(row, col.key) }}</span>
                    } @else {
                      {{ cell(row, col.key) }}
                    }
                  </td>
                }
                @if (hasActions()) {
                  <td class="actions-col">
                    <div class="actions">
                      @if (rowActions(); as tpl) {
                        <ng-container *ngTemplateOutlet="tpl; context: { $implicit: row }" />
                      }
                    </div>
                  </td>
                }
              </tr>
            }
          }
        </tbody>
      </table>

      @if (!loading() && rows().length === 0) {
        <div class="empty">
          <app-icon name="inbox" [size]="36" />
          <p>{{ emptyMessage() }}</p>
        </div>
      }
    </div>
  `,
})
export class TableComponent<T> {
  readonly columns = input.required<TableColumn<T>[]>();
  readonly rows = input<T[]>([]);
  readonly loading = input(false);
  readonly sortKey = input<string | null>(null);
  readonly sortDirection = input<SortDirection>('asc');
  readonly hasActions = input(false);
  readonly emptyMessage = input('No records found.');

  readonly sortChange = output<SortChange>();

  protected readonly rowActions = contentChild<TemplateRef<{ $implicit: T }>>('rowActions');
  protected readonly skeletonRows = [1, 2, 3, 4, 5];

  protected cell(row: T, key: string): unknown {
    return (row as Record<string, unknown>)[key];
  }

  protected isActive(v: unknown): boolean {
    return v === true || String(v).toLowerCase() === 'active';
  }

  protected statusLabel(v: unknown): string {
    return typeof v === 'boolean' ? (v ? 'Active' : 'Inactive') : String(v ?? '');
  }

  protected sortIcon(key: string) {
    if (this.sortKey() !== key) return 'arrow-up-down' as const;
    return this.sortDirection() === 'asc' ? ('arrow-up' as const) : ('arrow-down' as const);
  }

  protected ariaSort(key: string): string | null {
    if (this.sortKey() !== key) return null;
    return this.sortDirection() === 'asc' ? 'ascending' : 'descending';
  }

  protected toggleSort(key: string): void {
    const direction: SortDirection = this.sortKey() === key && this.sortDirection() === 'asc' ? 'desc' : 'asc';
    this.sortChange.emit({ key, direction });
  }
}
