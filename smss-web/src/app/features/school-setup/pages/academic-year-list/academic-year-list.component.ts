import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import {
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  PaginationComponent,
  SortChange,
  SortDirection,
  TableColumn,
  TableComponent,
} from '../../../../shared/components';
import { FeeRepairModalComponent } from '../../components/fee-repair-modal/fee-repair-modal.component';
import { YearCopyWizardComponent } from '../../components/year-copy-wizard/year-copy-wizard.component';
import { AcademicYearFormModalComponent } from '../../components/academic-year-form-modal/academic-year-form-modal.component';
import { AcademicYear, AcademicYearRow, AcademicYearState, AcademicYearUsage } from '../../models/academic-year.model';
import { YearContextService } from '../../../../core/services/year-context.service';
import { AcademicYearService } from '../../services/academic-year.service';

const COLUMNS: TableColumn<AcademicYearRow>[] = [
  { key: 'yearName', label: 'Year Name', sortable: true },
  { key: 'startDate', label: 'Start Date', sortable: true },
  { key: 'endDate', label: 'End Date', sortable: true },
  { key: 'stateLabel', label: 'State', type: 'badge', tone: (v) => STATE_TONE[String(v)] ?? 'gray', sortable: true },
  { key: 'usageText', label: 'In use' },
  { key: 'statusName', label: 'Status', type: 'status', sortable: true },
];

const STATE_LABEL: Record<AcademicYearState, string> = { CLOSED: 'Closed', CURRENT: 'Current', UPCOMING: 'Upcoming', PAST: 'Past', IN_SESSION: 'In session' };
const STATE_TONE: Record<string, string> = { Closed: 'gray-strike', Current: 'green', Upcoming: 'blue', Past: 'gray', 'In session': 'violet' };

/** "12 enrolments, 3 fee structures" or an em dash: what already depends on the year. */
export function usageSummary(u: AcademicYearUsage | undefined): string {
  if (!u) return '—';
  const parts: string[] = [];
  const add = (n: number, one: string, many: string) => { if (n > 0) parts.push(`${n} ${n === 1 ? one : many}`); };
  add(u.enrolments, 'enrolment', 'enrolments');
  add(u.feeStructures, 'fee structure', 'fee structures');
  add(u.attendanceRows, 'attendance record', 'attendance records');
  add(u.timetableEntries, 'timetable slot', 'timetable slots');
  add(u.classTeachers, 'class teacher row', 'class teacher rows');
  add(u.homework, 'homework', 'homework');
  add(u.tests, 'class test', 'class tests');
  add(u.events, 'event', 'events');
  return parts.length ? parts.join(', ') : '—';
}

/** Sort on the raw ISO date, not the formatted text shown in the cell. */
const SORT_KEY: Record<string, keyof AcademicYear> = { startDate: 'startDate', endDate: 'endDate' };

const formatDate = (iso: string): string => {
  const d = new Date(`${iso.slice(0, 10)}T00:00:00`);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
};

/** The signed-in school's own academic years (the server scopes everything to the token's school). */
@Component({
  selector: 'app-academic-year-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent, AcademicYearFormModalComponent, YearCopyWizardComponent, FeeRepairModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './academic-year-list.component.scss',
  templateUrl: './academic-year-list.component.html',
})
export class AcademicYearListComponent implements OnInit {
  private readonly service = inject(AcademicYearService);
  /** The header's viewing year follows the current flag: tell it whenever this list changes (set current, close, deactivate, add). */
  private readonly yearContext = inject(YearContextService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Academic Year' }];

  protected readonly years = signal<AcademicYear[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { year: null } = create; { year } = edit */
  protected readonly modal = signal<{ year: AcademicYear | null } | null>(null);
  /** The year the "Copy configuration from a previous year" wizard is open for (offered right after a year is created, and from a row). */
  protected readonly copyTarget = signal<AcademicYear | null>(null);
  protected readonly repairTarget = signal<AcademicYear | null>(null);

  protected readonly search = signal('');
  protected readonly sortKey = signal<string | null>('startDate');
  protected readonly sortDirection = signal<SortDirection>('desc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const rows = this.years().filter(
      (y) => !q || [y.yearName, formatDate(y.startDate), formatDate(y.endDate), y.statusName ?? '', STATE_LABEL[y.state] ?? ''].some((t) => t.toLowerCase().includes(q)),
    );
    if (!key) return rows;

    const raw = SORT_KEY[key];
    return [...rows].sort((a, b) => {
      const av = String(raw ? a[raw] : this.toRow(a)[key] ?? '');
      const bv = String(raw ? b[raw] : this.toRow(b)[key] ?? '');
      return av.localeCompare(bv, undefined, { sensitivity: 'base', numeric: true }) * dir;
    });
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed<AcademicYearRow[]>(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize()).map((y) => this.toRow(y));
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .list()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (years) => {
          this.years.set(years);
          this.yearContext.refresh();
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private toRow(y: AcademicYear): AcademicYearRow {
    const isActive = y.statusName === 'Active';
    return {
      academicYearId: y.academicYearId,
      yearName: y.yearName,
      startDate: formatDate(y.startDate),
      endDate: formatDate(y.endDate),
      currentLabel: y.isCurrent ? '★ Current' : '—',
      isClosed: y.isClosed,
      state: y.state,
      stateLabel: STATE_LABEL[y.state] ?? y.state,
      usageText: usageSummary(y.usage),
      statusName: y.statusName ?? 'Inactive',
      isCurrent: y.isCurrent,
      isActive,
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
    this.modal.set({ year: null });
  }

  protected openEdit(row: AcademicYearRow): void {
    this.modal.set({ year: this.years().find((y) => y.academicYearId === row.academicYearId) ?? null });
  }

  protected onSaved(saved?: AcademicYear): void {
    const created = this.modal()?.year === null;
    const others = this.years().filter((y) => y.academicYearId !== saved?.academicYearId).length;
    this.modal.set(null);
    // a new year (and there is an earlier one to copy from): offer to start it from that year's setup
    if (created && saved && others > 0 && saved.statusName === 'Active') this.copyTarget.set(saved);
    // The first-ever year becomes current server-side; reload so every row's flags stay authoritative
    this.load();
  }

  protected openCopy(row: AcademicYearRow): void {
    this.copyTarget.set(this.years().find((y) => y.academicYearId === row.academicYearId) ?? null);
  }

  protected openRepair(row: AcademicYearRow): void {
    this.repairTarget.set(this.years().find((y) => y.academicYearId === row.academicYearId) ?? null);
  }

  protected async toggleStatus(row: AcademicYearRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} academic year?`,
      message: deactivating
        ? `"${row.yearName}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
        : `"${row.yearName}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.run(row.academicYearId, this.service.toggleStatus(row.academicYearId), (y) => `${y.yearName} is now ${y.statusName}.`);
  }

  protected async close(row: AcademicYearRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Close academic year?',
      message: `"${row.yearName}" will be closed: its classes, class teachers, timetable, attendance, homework, class tests and new admissions can no longer be changed. Fee payments, receipts and every report stay available, and you can reopen the year later.`,
      confirmText: 'Close year',
      variant: 'danger',
    });
    if (!confirmed) return;
    this.run(row.academicYearId, this.service.close(row.academicYearId), (y) => `${y.yearName} is now closed.`);
  }

  protected async reopen(row: AcademicYearRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Reopen academic year?',
      message: `"${row.yearName}" will accept changes again.`,
      confirmText: 'Reopen',
    });
    if (!confirmed) return;
    this.run(row.academicYearId, this.service.reopen(row.academicYearId), (y) => `${y.yearName} is open again.`);
  }

  protected async setCurrent(row: AcademicYearRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Set as current academic year?',
      message: `"${row.yearName}" will become the current academic year for your school. The year that is current now will no longer be current.`,
      confirmText: 'Set as current',
    });
    if (!confirmed) return;

    // Other rows change too (the old current year), so the whole list is refreshed afterwards
    this.run(row.academicYearId, this.service.setCurrent(row.academicYearId), (y) => `${y.yearName} is now the current academic year.`, true);
  }

  private run(id: number, request$: ReturnType<AcademicYearService['toggleStatus']>, message: (y: AcademicYear) => string, reload = false): void {
    this.busyId.set(id);
    request$.pipe(finalize(() => this.busyId.set(null))).subscribe({
      next: (updated) => {
        this.years.update((list) => list.map((y) => (y.academicYearId === updated.academicYearId ? updated : y)));
        this.toast.success(message(updated));
        if (reload) this.load();
        else this.yearContext.refresh();
      },
      // The server's refusal (e.g. "Set a different year as current…") is already toasted by the interceptor
      error: () => undefined,
    });
  }
}
