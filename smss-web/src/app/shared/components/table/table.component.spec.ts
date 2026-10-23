import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TableColumn, TableComponent } from './table.component';

interface Row extends Record<string, unknown> {
  name: string;
  teacher: string;
}

@Component({
  imports: [TableComponent],
  template: `
    <app-table [columns]="columns" [rows]="rows">
      <ng-template #cell let-row let-key="key"><button class="pick" [attr.data-key]="key">{{ row.name }}:{{ row.teacher || 'none' }}</button></ng-template>
    </app-table>
  `,
})
class HostComponent {
  columns: TableColumn<Row>[] = [
    { key: 'name', label: 'Name' },
    { key: 'teacher', label: 'Teacher', type: 'template' },
  ];
  rows: Row[] = [{ name: 'A', teacher: 'Anita' }, { name: 'B', teacher: '' }];
}

describe('TableComponent template cells', () => {
  let fixture: ComponentFixture<HostComponent>;
  beforeEach(() => {
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it("renders the parent's #cell template for 'template' columns only, with the row and the column key", () => {
    const buttons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button.pick')];
    expect(buttons.map((b) => b.textContent)).toEqual(['A:Anita', 'B:none']);
    expect(buttons.every((b) => b.getAttribute('data-key') === 'teacher')).toBe(true);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('td')).toHaveLength(4);   // the plain Name cells are untouched
  });
});
