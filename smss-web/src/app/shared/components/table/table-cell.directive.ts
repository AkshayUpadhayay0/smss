import { Directive, TemplateRef, inject, input } from '@angular/core';

export interface TableCellContext<T = any> {
  $implicit: T;
  value: unknown;
}

/**
 * Custom cell renderer for one column of <app-table>:
 *
 *   <ng-template appTableCell="status" let-row>
 *     <app-badge>{{ row.status }}</app-badge>
 *   </ng-template>
 *
 * Columns without a matching template render their plain value.
 */
@Directive({
  selector: 'ng-template[appTableCell]',
  standalone: true,
})
export class TableCellDirective {
  /** Column key this template renders. */
  readonly appTableCell = input.required<string>();
  readonly template = inject<TemplateRef<TableCellContext>>(TemplateRef);

  static ngTemplateContextGuard(_dir: TableCellDirective, ctx: unknown): ctx is TableCellContext {
    return true;
  }
}
