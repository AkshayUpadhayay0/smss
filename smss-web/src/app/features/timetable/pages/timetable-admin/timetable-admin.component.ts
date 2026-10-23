import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize, forkJoin } from 'rxjs';
import { CardComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { ClassService } from '../../../school-setup/services/class.service';
import { SectionService } from '../../../school-setup/services/section.service';
import { Section } from '../../../school-setup/models/section.model';
import { SlotModalComponent, SlotTarget } from '../../components/slot-modal/slot-modal.component';
import { TimetableGridComponent } from '../../components/timetable-grid/timetable-grid.component';
import { TimetableCell, TimetableGrid, TimetableRow } from '../../models/timetable.model';
import { TimetableService } from '../../services/timetable.service';

/** School Admin: pick a year, class and section, then click any cell of the weekly grid to assign / change / clear it. */
@Component({
  selector: 'app-timetable-admin',
  imports: [ReactiveFormsModule, CardComponent, PageHeaderComponent, SelectComponent, TimetableGridComponent, SlotModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './timetable-admin.component.scss',
  template: `
    <app-page-header title="Timetable" subtitle="Plan each section's week: subject, teacher and room for every period" icon="calendar" [breadcrumbs]="breadcrumbs" />

    <app-card>
      <div class="pickers">
        <app-select label="Academic year" placeholder="Select year…" [options]="yearOptions()" [formControl]="yearControl" />
        <app-select label="Class" placeholder="Select class…" [options]="classOptions()" [formControl]="classControl" />
        <app-select label="Section" placeholder="Select section…" [options]="sectionOptions()" [formControl]="sectionControl" />
      </div>
    </app-card>

    <app-card>
      @if (!sectionId()) {
        <p class="note">Choose an academic year, a class and a section to see its timetable.</p>
      } @else if (loading()) {
        <p class="note" role="status">Loading timetable…</p>
      } @else if (loadFailed()) {
        <p class="note" role="alert">The timetable could not be loaded.</p>
      } @else if (grid(); as g) {
        @if (g.rows.length === 0) {
          <p class="note">No periods are set up yet. Add them under School Setup → Periods.</p>
        } @else {
          <p class="summary">{{ g.className }} – {{ g.sectionName }} · {{ g.academicYearName }} · {{ g.entryCount }} {{ g.entryCount === 1 ? 'slot' : 'slots' }} filled. Click a cell to edit it.</p>
          <app-timetable-grid [grid]="g" [selectable]="true" (cellClick)="openSlot($event.row, $event.cell)" />
        }
      }
    </app-card>

    @if (slot(); as s) {
      <app-slot-modal [target]="s" (saved)="onSlotSaved()" (closed)="slot.set(null)" />
    }
  `,
})
export class TimetableAdminComponent implements OnInit {
  private readonly service = inject(TimetableService);
  private readonly years = inject(AcademicYearService);
  private readonly classes = inject(ClassService);
  private readonly sections = inject(SectionService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Timetable' }];

  protected readonly yearControl = new FormControl('', { nonNullable: true });
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly sectionControl = new FormControl('', { nonNullable: true });

  protected readonly yearOptions = signal<SelectOption[]>([]);
  protected readonly classOptions = signal<SelectOption[]>([]);
  private readonly allSections = signal<Section[]>([]);
  // A FormControl's value is not reactive, so the choices are mirrored into signals.
  private readonly classId = signal('');
  private readonly yearId = signal('');
  protected readonly sectionId = signal('');

  protected readonly sectionOptions = computed<SelectOption[]>(() =>
    this.allSections()
      .filter((s) => s.statusName === 'Active' && String(s.classId) === this.classId())
      .map((s) => ({ label: s.sectionName, value: String(s.sectionId) })),
  );

  protected readonly grid = signal<TimetableGrid | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly slot = signal<SlotTarget | null>(null);

  ngOnInit(): void {
    this.yearControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.yearId.set(v);
      this.loadGrid();
    });
    this.classControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.classId.set(v);
      this.sectionControl.setValue('');
    });
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.sectionId.set(v);
      this.loadGrid();
    });

    forkJoin({ years: this.years.list(), classes: this.classes.list(), sections: this.sections.list() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ years, classes, sections }) => {
          const activeYears = years.filter((y) => y.statusName === 'Active');
          this.yearOptions.set(activeYears.map((y) => ({ label: y.isCurrent ? `${y.yearName} (current)` : y.yearName, value: String(y.academicYearId) })));
          this.classOptions.set(
            [...classes]
              .filter((c) => c.statusName === 'Active')
              .sort((a, b) => a.sequenceOrder - b.sequenceOrder)
              .map((c) => ({ label: c.className, value: String(c.classId) })),
          );
          this.allSections.set(sections);
          const year = activeYears.find((y) => y.isCurrent) ?? activeYears[0];
          if (year) this.yearControl.setValue(String(year.academicYearId));
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }

  protected loadGrid(): void {
    const section = Number(this.sectionId());
    const year = Number(this.yearId());
    if (!section || !year) {
      this.grid.set(null);
      return;
    }
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .classGrid(section, year)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (g) => this.grid.set(g),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected openSlot(row: TimetableRow, cell: TimetableCell): void {
    const g = this.grid();
    if (!g || !g.sectionId) return;
    this.slot.set({
      academicYearId: g.academicYearId,
      sectionId: g.sectionId,
      sectionLabel: `${g.className} – ${g.sectionName}`,
      dayOfWeek: cell.dayOfWeek,
      dayName: g.days.find((d) => d.dayOfWeek === cell.dayOfWeek)?.name ?? '',
      periodId: row.period.periodId,
      periodNumber: row.period.periodNumber,
      current: cell.entry,
    });
  }

  protected onSlotSaved(): void {
    this.slot.set(null);
    this.loadGrid(); // server truth
  }
}
