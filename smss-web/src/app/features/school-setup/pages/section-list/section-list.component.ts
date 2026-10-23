import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { finalize, forkJoin } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import {
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  PaginationComponent,
  SelectComponent,
  SelectOption,
  SortChange,
  SortDirection,
  TableColumn,
  TableComponent,
} from '../../../../shared/components';
import { SectionFormModalComponent } from '../../components/section-form-modal/section-form-modal.component';
import { ClassTeacherSection, EligibleTeacher } from '../../models/class-teacher.model';
import { Section, SectionRow } from '../../models/section.model';
import { AcademicYearService } from '../../services/academic-year.service';
import { ClassTeacherService } from '../../services/class-teacher.service';
import { SectionService } from '../../services/section.service';

const COLUMNS: TableColumn<SectionRow>[] = [
  { key: 'className', label: 'Class', sortable: true },
  { key: 'sectionName', label: 'Section Name', sortable: true },
  { key: 'maxStrength', label: 'Max Strength', sortable: true },
  { key: 'classTeacher', label: 'Class Teacher', sortable: true, type: 'template', width: '330px' },
  { key: 'statusName', label: 'Status', type: 'status', sortable: true },
];

const byText = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: 'base', numeric: true });

/** The signed-in school's own sections (the server scopes everything to the token's school). */
@Component({
  selector: 'app-section-list',
  imports: [FormsModule, ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent, SectionFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './section-list.component.scss',
  templateUrl: './section-list.component.html',
})
export class SectionListComponent implements OnInit {
  private readonly service = inject(SectionService);
  private readonly classTeachers = inject(ClassTeacherService);
  private readonly years = inject(AcademicYearService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Section' }];

  protected readonly sections = signal<Section[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);

  // ---- class teachers (per academic year)
  protected readonly yearControl = new FormControl('', { nonNullable: true });
  protected readonly yearOptions = signal<SelectOption[]>([]);
  /** sectionId -> its class teacher for the chosen year (active sections only). */
  private readonly assignments = signal<ReadonlyMap<number, ClassTeacherSection>>(new Map());
  private readonly teachers = signal<EligibleTeacher[]>([]);
  protected readonly teacherLoading = signal(false);
  protected readonly teacherFailed = signal(false);
  protected readonly teacherSavingId = signal<number | null>(null);
  /** Bumped to re-create a row's dropdown, so it snaps back to the saved value after a failed change. */
  protected readonly dropdownVersion = signal(0);
  /** null = closed; { section: null } = create; { section } = edit */
  protected readonly modal = signal<{ section: Section | null } | null>(null);

  protected readonly search = signal('');
  // The Class column sorts in the classes' own display order (Nursery before Class 1), then by section name
  protected readonly sortKey = signal<string | null>('className');
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const rows = this.sections().map((s) => this.toRow(s)).filter(
      (r) => !q || [r.className, r.sectionName, r.maxStrength, r.statusName, r.classTeacher].some((t) => t.toLowerCase().includes(q)),
    );
    if (!key) return rows;

    return [...rows].sort((a, b) => {
      let primary: number;
      if (key === 'className') primary = a.classSequenceOrder - b.classSequenceOrder || byText(a.className, b.className);
      else if (key === 'classTeacher') primary = byText(a.classTeacher || '\uffff', b.classTeacher || '\uffff'); // sections WITHOUT a teacher sort last when ascending
      else if (key === 'maxStrength') primary = (Number(a.maxStrength) || 0) - (Number(b.maxStrength) || 0); // blanks (0) first when ascending
      else primary = byText(String(a[key] ?? ''), String(b[key] ?? ''));
      return primary ? primary * dir : byText(a.sectionName, b.sectionName);
    });
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed<SectionRow[]>(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  /** Active sections that have no class teacher for the chosen year: flagged so they are not forgotten. */
  protected readonly missingCount = computed(() => this.sections().filter((s) => s.statusName === 'Active' && !this.assignments().get(s.sectionId)?.hasClassTeacher).length);
  /** The chosen year as a signal (a FormControl's value is not reactive, so computed() could not see it change). */
  private readonly selectedYear = signal('');
  protected readonly showMissing = computed(() => !!this.selectedYear() && !this.teacherLoading() && !this.teacherFailed() && this.missingCount() > 0 && this.assignmentsLoaded());
  private readonly assignmentsLoaded = signal(false);

  ngOnInit(): void {
    this.load();
    this.yearControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.selectedYear.set(v);
      this.assignmentsLoaded.set(false);
      this.loadClassTeachers();
    });
    this.years.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (years) => {
        const active = years.filter((y) => y.statusName === 'Active');
        this.yearOptions.set(active.map((y) => ({ label: y.isCurrent ? `${y.yearName} (current)` : y.yearName, value: String(y.academicYearId) })));
        const year = active.find((y) => y.isCurrent) ?? active[0];
        if (year) this.yearControl.setValue(String(year.academicYearId)); // emits: loads the class teachers
      },
      error: () => undefined, // the list still works without the class teacher column
    });
  }

  /** The chosen year's class teachers, and who can be one. */
  protected loadClassTeachers(): void {
    const year = Number(this.yearControl.value);
    if (!year) return;
    this.teacherLoading.set(true);
    this.teacherFailed.set(false);
    forkJoin({ list: this.classTeachers.list(year), teachers: this.classTeachers.eligibleTeachers() })
      .pipe(finalize(() => this.teacherLoading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ list, teachers }) => {
          this.assignments.set(new Map(list.sections.map((s) => [s.sectionId, s])));
          this.teachers.set(teachers);
          this.assignmentsLoaded.set(true);
          this.dropdownVersion.update((v) => v + 1);
        },
        error: () => this.teacherFailed.set(true), // already toasted by the interceptor
      });
  }

  /** The dropdown's choices for one section: eligible teachers, plus its current teacher if they are no longer eligible (so the value still shows). */
  protected teacherOptions(row: SectionRow): SelectOption[] {
    const options: SelectOption[] = this.teachers().map((t) => ({ label: `${t.fullName} (${t.employeeId})`, value: t.employeeId }));
    if (row.classTeacherId && !options.some((o) => o.value === row.classTeacherId)) options.unshift({ label: `${row.classTeacher} (no longer eligible)`, value: row.classTeacherId, disabled: true });
    return options;
  }

  protected onTeacherChange(row: SectionRow, employeeId: string): void {
    const year = Number(this.yearControl.value);
    if (!year || this.teacherSavingId() !== null || employeeId === row.classTeacherId) return;
    const section = this.sections().find((s) => s.sectionId === row.sectionId);
    if (!section) return;

    this.teacherSavingId.set(row.sectionId);
    const request$ = employeeId ? this.classTeachers.assign(year, section.classId, section.sectionId, employeeId) : this.classTeachers.unassign(year, section.sectionId);
    request$.pipe(finalize(() => this.teacherSavingId.set(null)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (saved) => {
        this.toast.success(employeeId ? `${saved.employeeName} is now the class teacher of ${row.className} – ${row.sectionName}.` : `Class teacher removed from ${row.className} – ${row.sectionName}.`);
        this.loadClassTeachers(); // server truth: names, and the missing-teacher flags
      },
      error: () => this.dropdownVersion.update((v) => v + 1), // the reason is toasted by the interceptor; put the dropdown back on the saved teacher
    });
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .list()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (sections) => this.sections.set(sections),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private toRow(s: Section): SectionRow {
    const teacher = s.statusName === 'Active' ? this.assignments().get(s.sectionId) : undefined;
    return {
      sectionId: s.sectionId,
      classSequenceOrder: s.classSequenceOrder,
      className: s.className,
      sectionName: s.sectionName,
      maxStrength: s.maxStrength == null ? '' : String(s.maxStrength),
      statusName: s.statusName ?? 'Inactive',
      isActive: s.statusName === 'Active',
      classTeacherId: teacher?.employeeId ?? '',
      classTeacher: teacher?.employeeName ?? '',
    };
  }

  protected onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.page.set(1);
  }

  protected onSort(change: SortChange): void {
    this.sortKey.set(change.key);
    this.sortDirection.set(change.direction);
    this.page.set(1);
  }

  protected onPageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
  }

  protected openCreate(): void {
    this.modal.set({ section: null });
  }

  protected openEdit(row: SectionRow): void {
    this.modal.set({ section: this.sections().find((s) => s.sectionId === row.sectionId) ?? null });
  }

  protected onSaved(saved: Section): void {
    this.modal.set(null);
    this.sections.update((list) => (list.some((s) => s.sectionId === saved.sectionId) ? list.map((s) => (s.sectionId === saved.sectionId ? saved : s)) : [...list, saved]));
  }

  protected async toggleStatus(row: SectionRow): Promise<void> {
    const deactivating = row.isActive;
    const label = `${row.className} – ${row.sectionName}`;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} section?`,
      message: deactivating
        ? `"${label}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
        : `"${label}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.sectionId);
    this.service
      .toggleStatus(row.sectionId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.sections.update((list) => list.map((s) => (s.sectionId === updated.sectionId ? updated : s)));
          this.toast.success(`${label} is now ${updated.statusName}.`);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
