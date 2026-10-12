import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, finalize, forkJoin, of } from 'rxjs';
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
import { SchoolClass } from '../../../school-setup/models/class.model';
import { Section } from '../../../school-setup/models/section.model';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { ClassService } from '../../../school-setup/services/class.service';
import { SectionService } from '../../../school-setup/services/section.service';
import { toUploadUrl } from '../../../../core/utils/upload-url';
import { StudentListItem, StudentRow } from '../../models/student.model';
import { StudentService } from '../../services/student.service';

const COLUMNS: TableColumn<StudentRow>[] = [
  { key: 'photoUrl', label: '', type: 'avatar', width: '64px', avatarLabel: (r) => r.fullName },
  { key: 'rollNumber', label: 'Roll Number', sortable: true, width: '130px' },
  { key: 'admissionNumber', label: 'Admission Number', sortable: true },
  { key: 'fullName', label: 'Name', sortable: true },
  { key: 'genderName', label: 'Gender', sortable: true },
  { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
];

const byText = (a: unknown, b: unknown) => String(a ?? '').localeCompare(String(b ?? ''), undefined, { sensitivity: 'base', numeric: true });

/** A school's students for one academic year, filtered Academic Year → Class → Section. */
@Component({
  selector: 'app-student-list',
  imports: [
    ReactiveFormsModule,
    ButtonComponent,
    CardComponent,
    IconComponent,
    InputComponent,
    PageHeaderComponent,
    PaginationComponent,
    SelectComponent,
    TableComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-list.component.scss',
  templateUrl: './student-list.component.html',
})
export class StudentListComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly years = inject(AcademicYearService);
  private readonly classes = inject(ClassService);
  private readonly sections = inject(SectionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Students' }];

  // ── Filters ─────────────────────────────────────────────────────────
  protected readonly yearControl = new FormControl('', { nonNullable: true });
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  private readonly yearValue = toSignal(this.yearControl.valueChanges, { initialValue: '' });
  private readonly classValue = toSignal(this.classControl.valueChanges, { initialValue: '' });
  private readonly sectionValue = toSignal(this.sectionControl.valueChanges, { initialValue: '' });

  protected readonly yearOptions = signal<SelectOption[]>([]);
  protected readonly classOptions = signal<SelectOption[]>([]);
  private readonly allSections = signal<Section[]>([]);
  /** Sections of the chosen class only (a section belongs to one class). */
  protected readonly sectionOptions = computed<SelectOption[]>(() => {
    const classId = this.classValue();
    if (!classId) return [];
    return this.allSections()
      .filter((s) => String(s.classId) === classId && s.statusName === 'Active')
      .map((s) => ({ label: s.sectionName, value: String(s.sectionId) }));
  });

  protected readonly setupLoading = signal(true);
  protected readonly ready = computed(() => !!this.yearValue() && !!this.classValue() && !!this.sectionValue());

  // ── Data ────────────────────────────────────────────────────────────
  protected readonly items = signal<StudentListItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);

  protected readonly search = signal('');
  protected readonly sortKey = signal<string | null>(null); // null = server order (roll number, then name)
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly rows = computed<StudentRow[]>(() =>
    this.items().map((s) => ({
      studentId: s.studentId,
      photoUrl: toUploadUrl(s.profilePhotoUrl) ?? '',
      rollNumber: s.rollNumber ?? '',
      admissionNumber: s.admissionNumber,
      fullName: s.fullName,
      genderName: s.genderName ?? '',
      statusName: s.statusName,
    })),
  );

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const rows = q ? this.rows().filter((r) => [r.rollNumber, r.admissionNumber, r.fullName, r.genderName, r.statusName].some((t) => t.toLowerCase().includes(q))) : this.rows();
    if (!key) return rows;
    return [...rows].sort((a, b) => {
      const av = String(a[key] ?? '');
      const bv = String(b[key] ?? '');
      if (!av && bv) return 1; // blanks last in both directions
      if (av && !bv) return -1;
      return byText(av, bv) * dir;
    });
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  protected readonly emptyMessage = computed(() =>
    this.loadFailed() ? 'Could not load students.' : this.search() ? 'Nothing matches your search.' : 'No students are enrolled in this section yet.',
  );

  ngOnInit(): void {
    this.classControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.sectionControl.setValue(''));
    this.yearControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.reload());
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.reload());
    this.loadSetup();
  }

  /** Loads the years, classes and sections once, then restores the filter the user came back from (query params). */
  private loadSetup(): void {
    forkJoin({
      years: this.years.list().pipe(catchError(() => of([] as AcademicYear[]))),
      classes: this.classes.list().pipe(catchError(() => of([] as SchoolClass[]))),
      sections: this.sections.list().pipe(catchError(() => of([] as Section[]))),
    })
      .pipe(finalize(() => this.setupLoading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe(({ years, classes, sections }) => {
        const activeYears = years.filter((y) => y.statusName === 'Active');
        this.yearOptions.set(activeYears.map((y) => ({ label: y.isCurrent ? `${y.yearName} (current)` : y.yearName, value: String(y.academicYearId) })));
        this.classOptions.set(classes.filter((c) => c.statusName === 'Active').map((c) => ({ label: c.className, value: String(c.classId) })));
        this.allSections.set(sections);

        const q = this.route.snapshot.queryParamMap;
        const wantedYear = q.get('academicYearId');
        const year = activeYears.find((y) => String(y.academicYearId) === wantedYear) ?? activeYears.find((y) => y.isCurrent);
        // These must emit: `ready` is derived from the controls' valueChanges, so a silent set would leave it false
        if (year) this.yearControl.setValue(String(year.academicYearId));

        const wantedClass = q.get('classId');
        if (wantedClass && this.classOptions().some((o) => o.value === wantedClass)) {
          const wantedSection = q.get('sectionId');
          this.classControl.setValue(wantedClass); // clears the section, then the saved one is applied below
          if (wantedSection && this.sectionOptions().some((o) => o.value === wantedSection)) this.sectionControl.setValue(wantedSection);
        }
      });
  }

  private reload(): void {
    this.items.set([]);
    this.loadFailed.set(false);
    this.page.set(1);
    if (!this.ready()) return;

    this.loading.set(true);
    this.students
      .list(Number(this.yearControl.value), Number(this.sectionControl.value))
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => this.items.set(list),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected retry(): void {
    this.reload();
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

  /** Carries the filter in the URL so the profile's "Back to Students" returns to the same list. */
  protected view(row: StudentRow): void {
    void this.router.navigate(['/students', row.studentId], {
      queryParams: { academicYearId: this.yearControl.value, classId: this.classControl.value, sectionId: this.sectionControl.value },
    });
  }
}
