import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
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
import { AdmissionActionsComponent } from '../../components/admission-actions/admission-actions.component';
import { Admission, AdmissionDetail, AdmissionStatus, STATUS_META, formatDate } from '../../models/admission.model';
import { AdmissionService } from '../../services/admission.service';

interface AdmissionRow extends Record<string, unknown> {
  admissionId: number;
  applicationNumber: string;
  applicantFullName: string;
  appliedClassName: string;
  admissionTypeName: string;
  applicationDateText: string;
  statusLabel: string;
  statusName: AdmissionStatus;
}

const COLUMNS: TableColumn<AdmissionRow>[] = [
  { key: 'applicationNumber', label: 'Application Number', sortable: true },
  { key: 'applicantFullName', label: 'Applicant Name', sortable: true },
  { key: 'appliedClassName', label: 'Applied Class', sortable: true },
  { key: 'admissionTypeName', label: 'Admission Type', sortable: true },
  { key: 'applicationDateText', label: 'Application Date', sortable: true },
  {
    key: 'statusLabel',
    label: 'Status',
    sortable: true,
    type: 'badge',
    tone: (label) => Object.values(STATUS_META).find((m) => m.label === label)?.tone ?? 'gray',
  },
];

const byText = (a: unknown, b: unknown) => String(a ?? '').localeCompare(String(b ?? ''), undefined, { sensitivity: 'base', numeric: true });

/** The school's admission applications, with the workflow actions valid for each row's status. */
@Component({
  selector: 'app-admission-list',
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
    AdmissionActionsComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './admission-list.component.scss',
  templateUrl: './admission-list.component.html',
})
export class AdmissionListComponent implements OnInit {
  private readonly service = inject(AdmissionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Admissions' }];
  protected readonly statusOptions: SelectOption[] = (Object.keys(STATUS_META) as AdmissionStatus[]).map((s) => ({ label: STATUS_META[s].label, value: s }));

  protected readonly admissions = signal<Admission[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly search = signal('');
  /** '' = all statuses. */
  protected readonly statusFilter = signal('');
  protected readonly statusControl = new FormControl('', { nonNullable: true });
  /** '' = all classes / years. The options are the classes and years that actually have applications. */
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly yearControl = new FormControl('', { nonNullable: true });
  protected readonly classFilter = signal('');
  protected readonly yearFilter = signal('');
  protected readonly classOptions = computed<SelectOption[]>(() => this.distinctOptions((a) => [a.appliedClassId, a.appliedClassName]));
  protected readonly yearOptions = computed<SelectOption[]>(() => this.distinctOptions((a) => [a.academicYearId, a.academicYearName]));
  protected readonly sortKey = signal<string | null>('applicationDateText');
  protected readonly sortDirection = signal<SortDirection>('desc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const status = this.statusFilter();
    const cls = this.classFilter();
    const year = this.yearFilter();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const rows = this.admissions()
      .filter((a) => !status || a.statusName === status)
      .filter((a) => !cls || String(a.appliedClassId) === cls)
      .filter((a) => !year || String(a.academicYearId) === year)
      .filter((a) => !q || [a.applicationNumber, a.applicantFullName, a.appliedClassName ?? '', a.admissionTypeName ?? ''].some((t) => t.toLowerCase().includes(q)));

    const sorted = [...rows];
    if (key) {
      sorted.sort((a, b) => {
        // dates sort on the raw ISO value, not the formatted text
        const av = key === 'applicationDateText' ? a.applicationDate : this.toRow(a)[key];
        const bv = key === 'applicationDateText' ? b.applicationDate : this.toRow(b)[key];
        return byText(av, bv) * dir || b.admissionId - a.admissionId;
      });
    }
    return sorted.map((a) => this.toRow(a));
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  ngOnInit(): void {
    this.statusControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.statusFilter.set(v ?? '');
      this.page.set(1);
    });
    this.classControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.classFilter.set(v ?? '');
      this.page.set(1);
    });
    this.yearControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.yearFilter.set(v ?? '');
      this.page.set(1);
    });
    this.load();
  }

  private distinctOptions(pick: (a: Admission) => [number, string | null | undefined]): SelectOption[] {
    const seen = new Map<number, string>();
    for (const a of this.admissions()) {
      const [id, name] = pick(a);
      if (name) seen.set(id, name);
    }
    return [{ label: 'All', value: '' }, ...[...seen].sort((x, y) => x[1].localeCompare(y[1], undefined, { numeric: true })).map(([id, name]) => ({ label: name, value: String(id) }))];
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .getAll()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (list) => this.admissions.set(list ?? []),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private toRow(a: Admission): AdmissionRow {
    return {
      admissionId: a.admissionId,
      applicationNumber: a.applicationNumber,
      applicantFullName: a.applicantFullName,
      appliedClassName: a.appliedClassName ?? '',
      admissionTypeName: a.admissionTypeName ?? '',
      applicationDateText: formatDate(a.applicationDate),
      statusLabel: STATUS_META[a.statusName]?.label ?? a.statusName,
      statusName: a.statusName,
    };
  }

  /** The row's full record, for the actions component. */
  protected admissionOf(row: AdmissionRow): Admission {
    return this.admissions().find((a) => a.admissionId === row.admissionId)!;
  }

  protected onChanged(updated: AdmissionDetail): void {
    this.admissions.update((list) => list.map((a) => (a.admissionId === updated.admissionId ? { ...a, ...updated } : a)));
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

  protected newApplication(): void {
    void this.router.navigate(['/admissions/new']);
  }

  protected view(row: AdmissionRow): void {
    void this.router.navigate(['/admissions', row.admissionId]);
  }
}
