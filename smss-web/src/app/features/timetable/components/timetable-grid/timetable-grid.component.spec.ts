import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TimetableCell, TimetableGrid, TimetableRow } from '../../models/timetable.model';
import { TimetableGridComponent } from './timetable-grid.component';

const cells = (applies: (d: number) => boolean, entry?: (d: number) => TimetableCell['entry']): TimetableCell[] =>
  [1, 2, 3, 4, 5, 6].map((d) => ({ dayOfWeek: d, entry: entry?.(d) ?? null, applies: applies(d) }));

const subject = (day: number) => ({
  timetableEntryId: day, academicYearId: 7, classId: 1, className: 'Class 1', sectionId: 2, sectionName: 'A', dayOfWeek: day, periodId: 11, periodNumber: 1,
  subjectId: 3, subjectName: 'Maths', employeeId: 'E1', employeeName: 'Asha Rao',
});

function grid(rows: TimetableRow[]): TimetableGrid {
  return {
    academicYearId: 7, academicYearName: '2026-27', scope: 'section', entryCount: 0, rows,
    days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'].map((name, i) => ({ dayOfWeek: i + 1, name })),
  };
}

describe('TimetableGridComponent (breaks and schedules)', () => {
  let fixture: ComponentFixture<TimetableGridComponent>;
  const root = () => fixture.nativeElement as HTMLElement;

  function show(g: TimetableGrid, selectable = true) {
    fixture = TestBed.createComponent(TimetableGridComponent);
    fixture.componentRef.setInput('grid', g);
    fixture.componentRef.setInput('selectable', selectable);
    fixture.detectChanges();
  }

  it('a break row has no clickable cell and shows its label', () => {
    show(grid([
      { period: { periodId: 11, periodNumber: 1, startTime: '09:00:00', endTime: '09:45:00', slotType: 'PERIOD' }, cells: cells(() => true) },
      { period: { periodId: 12, periodNumber: 0, startTime: '10:30:00', endTime: '11:00:00', slotType: 'BREAK', label: 'Lunch' }, cells: cells(() => true) },
    ]));
    const rows = root().querySelectorAll('tbody tr');
    expect(rows[1].querySelectorAll('button.cell').length).toBe(0);
    expect(rows[1].querySelectorAll('[data-break]').length).toBe(6);
    expect(rows[1].textContent).toContain('Lunch');
    expect(rows[0].querySelectorAll('button.cell').length).toBe(6);   // an ordinary period stays clickable
    expect(rows[0].textContent).toContain('Period 1');
  });

  it('a slot that does not apply on a day is a dim, non-clickable cell, and a mixed grid names the schedule', () => {
    show(grid([
      { period: { periodId: 11, periodNumber: 1, startTime: '08:00:00', endTime: '08:40:00', slotType: 'PERIOD', scheduleName: 'Saturday', days: [6] }, cells: cells((d) => d === 6, (d) => (d === 6 ? subject(6) : null)) },
    ]));
    const row = root().querySelector('tbody tr')!;
    expect(row.querySelectorAll('[data-na]').length).toBe(5);
    expect(row.querySelectorAll('button.cell').length).toBe(1);
    expect(row.querySelector('button.cell')?.textContent).toContain('Maths');
    expect(row.textContent).toContain('Saturday');
  });

  it('an old-style row without the new fields still renders as a plain period', () => {
    show(grid([{ period: { periodId: 11, periodNumber: 2, startTime: '09:45:00', endTime: '10:30:00' }, cells: [1, 2, 3, 4, 5, 6].map((d) => ({ dayOfWeek: d, entry: null })) }]));
    expect(root().querySelectorAll('button.cell').length).toBe(6);
    expect(root().textContent).toContain('Period 2');
  });
});
