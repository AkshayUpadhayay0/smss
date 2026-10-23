import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TimetableCell, TimetableGrid, TimetableRow } from '../../models/timetable.model';

/** "08:00:00" -> "08:00" */
export const shortTime = (t: string): string => t.slice(0, 5);

/**
 * A weekly grid: one row per period, one column per day. Read-only unless `selectable`, in which case every cell is a
 * button (the admin page opens the assign dialog from it). Teacher-scope cells show the class and section; section-scope
 * cells show the teacher.
 */
@Component({
  selector: 'app-timetable-grid',
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './timetable-grid.component.scss',
  template: `
    <div class="scroll">
      <table class="grid">
        <thead>
          <tr>
            <th scope="col" class="period-head">Period</th>
            @for (d of grid().days; track d.dayOfWeek) {
              <th scope="col">{{ d.name }}</th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of grid().rows; track row.period.periodId) {
            <tr>
              <th scope="row" class="period-head">
                <span class="num">Period {{ row.period.periodNumber }}</span>
                <span class="time">{{ time(row.period.startTime) }} – {{ time(row.period.endTime) }}</span>
              </th>
              @for (cell of row.cells; track cell.dayOfWeek) {
                <td>
                  @if (selectable()) {
                    <button type="button" class="cell" [class.empty]="!cell.entry" (click)="cellClick.emit({ row, cell })" [attr.aria-label]="label(row, cell)">
                      <ng-container *ngTemplateOutlet="body; context: { $implicit: cell }" />
                    </button>
                  } @else {
                    <div class="cell static" [class.empty]="!cell.entry">
                      <ng-container *ngTemplateOutlet="body; context: { $implicit: cell }" />
                    </div>
                  }
                </td>
              }
            </tr>
          }
        </tbody>
      </table>
    </div>

    <ng-template #body let-cell>
      @if (cell.entry; as e) {
        <span class="subject">{{ e.subjectName }}</span>
        <span class="who">{{ grid().scope === 'teacher' ? e.className + ' – ' + e.sectionName : e.employeeName }}</span>
        @if (e.roomNo) {
          <span class="room">Room {{ e.roomNo }}</span>
        }
      } @else {
        <span class="free">{{ selectable() ? '+ Add' : '—' }}</span>
      }
    </ng-template>
  `,
})
export class TimetableGridComponent {
  readonly grid = input.required<TimetableGrid>();
  readonly selectable = input(false);
  readonly cellClick = output<{ row: TimetableRow; cell: TimetableCell }>();

  protected readonly time = shortTime;

  protected label(row: TimetableRow, cell: TimetableCell): string {
    const day = this.grid().days.find((d) => d.dayOfWeek === cell.dayOfWeek)?.name ?? '';
    return `${day}, period ${row.period.periodNumber}: ${cell.entry ? cell.entry.subjectName : 'empty'}`;
  }
}
