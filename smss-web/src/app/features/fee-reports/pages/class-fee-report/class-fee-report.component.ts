import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { canAccess } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption, SortChange, SortDirection, TableColumn, TableComponent } from '../../../../shared/components';
import { Section } from '../../../school-setup/models/section.model';
import { dueStatusTone } from '../../../students/models/student-billing.model';
import { ClassFeeReport, ClassFeeTableRow, STATUS_RANK, csvCell } from '../../models/fee-report.model';
import { FeeReportService } from '../../services/fee-report.service';

const COLUMNS: TableColumn<ClassFeeTableRow>[] = [
  { key: 'fullName', label: 'Student', sortable: true },
  { key: 'sectionName', label: 'Section', sortable: true, width: '90px' },
  { key: 'rollNumber', label: 'Roll', sortable: true, width: '80px' },
  { key: 'totalDue', label: 'Total Due', sortable: true },
  { key: 'totalCollected', label: 'Collected', sortable: true },
  { key: 'outstanding', label: 'Outstanding', sortable: true },
  { key: 'status', label: 'Status', sortable: true, type: 'badge', tone: dueStatusTone, width: '140px' },
];
const NUMERIC = new Set(['totalDue', 'totalCollected', 'outstanding']);
const byText = (a: unknown, b: unknown) => String(a ?? '').localeCompare(String(b ?? ''), undefined, { sensitivity: 'base', numeric: true });

/** Class-wise collection report: per student total due / collected / outstanding / status, with a class total. */
@Component({
  selector: 'app-class-fee-report',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './class-fee-report.component.scss',
  templateUrl: './class-fee-report.component.html',
})
export class ClassFeeReportComponent implements OnInit {
  private readonly reports = inject(FeeReportService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Fees' }, { label: 'Class-wise Report' }];
  protected readonly inr = formatInr;

  protected readonly yearControl = new FormControl('', { nonNullable: true });
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  protected readonly yearOptions = signal<SelectOption[]>([]);
  protected readonly classOptions = signal<SelectOption[]>([]);
  private readonly allSections = signal<Section[]>([]);
  private readonly classValue = signal('');
  protected readonly sectionOptions = computed<SelectOption[]>(() =>
    this.allSections()
      .filter((s) => String(s.classId) === this.classValue() && s.statusName === 'Active')
      .map((s) => ({ label: s.sectionName, value: String(s.sectionId) })),
  );

  protected readonly report = signal<ClassFeeReport | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly sortKey = signal<string | null>('outstanding');
  protected readonly sortDirection = signal<SortDirection>('desc'); // who owes the most first

  /** Drill-down into the student's Fees tab: managers only, and only for roles that can actually open the student page. */
  protected readonly canDrillDown = computed(() => !!this.report()?.canManage && canAccess('/students/x', this.auth.currentUserRoles()));
  protected readonly canExport = computed(() => !!this.report()?.canManage);

  private readonly rows = computed<ClassFeeTableRow[]>(() =>
    (this.report()?.students ?? []).map((s) => ({
      studentId: s.studentId,
      fullName: s.fullName,
      sectionName: s.sectionName,
      rollNumber: s.rollNumber ?? '',
      totalDue: s.status === 'Not Set' ? '—' : formatInr(s.totalDue),
      totalCollected: s.status === 'Not Set' ? '—' : formatInr(s.totalCollected),
      outstanding: s.status === 'Not Set' ? '—' : formatInr(s.outstanding),
      status: s.status,
      totalDueNum: s.totalDue,
      totalCollectedNum: s.totalCollected,
      outstandingNum: s.outstanding,
    })),
  );

  protected readonly sortedRows = computed(() => {
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    if (!key) return this.rows();
    return [...this.rows()].sort((a, b) => {
      if (NUMERIC.has(key)) return ((a[`${key}Num`] as number) - (b[`${key}Num`] as number)) * dir;
      if (key === 'status') return (STATUS_RANK[a.status] - STATUS_RANK[b.status]) * dir;
      return byText(a[key], b[key]) * dir;
    });
  });

  protected readonly counts = computed(() =>
    Object.entries(this.report()?.statusCounts ?? {}).filter(([, n]) => n > 0).map(([status, n]) => ({ status, n })),
  );

  ngOnInit(): void {
    this.yearControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.classControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.classValue.set(v);
      this.sectionControl.setValue('', { emitEvent: false });
      this.load();
    });

    this.reports
      .filterOptions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ years, classes, sections }) => {
        const active = years.filter((y) => y.statusName === 'Active');
        this.yearOptions.set(active.map((y) => ({ label: y.isCurrent ? `${y.yearName} (current)` : y.yearName, value: String(y.academicYearId) })));
        this.classOptions.set(classes.filter((c) => c.statusName === 'Active').map((c) => ({ label: c.className, value: String(c.classId) })));
        this.allSections.set(sections);

        // opened from the dashboard with its filter in the URL
        const q = this.route.snapshot.queryParamMap;
        const year = active.find((y) => String(y.academicYearId) === q.get('academicYearId')) ?? active.find((y) => y.isCurrent) ?? active[0];
        const wantedClass = q.get('classId');
        const wantedSection = q.get('sectionId');
        if (wantedClass && this.classOptions().some((o) => o.value === wantedClass)) {
          this.classValue.set(wantedClass);
          this.classControl.setValue(wantedClass, { emitEvent: false });
          if (wantedSection && this.sectionOptions().some((o) => o.value === wantedSection)) this.sectionControl.setValue(wantedSection, { emitEvent: false });
        }
        if (year) this.yearControl.setValue(String(year.academicYearId)); // emits: loads once everything above is in place
      });
  }

  protected load(): void {
    this.report.set(null);
    this.failed.set(false);
    if (!this.yearControl.value || !this.classControl.value) return;
    this.loading.set(true);
    this.reports
      .classReport(Number(this.classControl.value), Number(this.yearControl.value), Number(this.sectionControl.value) || null)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => this.report.set(r),
        error: () => this.failed.set(true), // already toasted by the interceptor
      });
  }

  protected onSort(change: SortChange): void {
    this.sortKey.set(change.key);
    this.sortDirection.set(change.direction);
  }

  protected emptyMessage(): string {
    if (this.failed()) return 'Could not load the report.';
    return this.classControl.value ? 'No students are enrolled in this class for the year.' : 'Select a class.';
  }

  protected view(row: ClassFeeTableRow): void {
    void this.router.navigate(['/students', row.studentId], {
      queryParams: { academicYearId: this.yearControl.value, classId: this.classControl.value, sectionId: this.sectionControl.value || null },
    });
  }

  /** Plain CSV built in the browser: no library, opens in Excel. */
  protected exportCsv(): void {
    const r = this.report();
    if (!r) return;
    const lines = [['Student ID', 'Student', 'Section', 'Roll', 'Total due', 'Collected', 'Outstanding', 'Status']];
    for (const s of this.sortedRows()) lines.push([s.studentId, s.fullName, s.sectionName, s.rollNumber, String(s.totalDueNum), String(s.totalCollectedNum), String(s.outstandingNum), s.status]);
    lines.push(['', 'Class total', '', '', String(r.totalDue), String(r.totalCollected), String(r.totalOutstanding), '']);
    const blob = new Blob(['\ufeff' + lines.map((l) => l.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `fee-report-${r.className}-${r.academicYearName}.csv`.replace(/[^\w.-]+/g, '_');
    a.click();
    URL.revokeObjectURL(url);
  }
}
