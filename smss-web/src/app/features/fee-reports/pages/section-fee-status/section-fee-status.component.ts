import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { AttendanceService } from '../../../attendance/services/attendance.service';
import { MarkableSection } from '../../../attendance/models/attendance.model';
import { csvField } from '../../../attendance/utils/attendance-report-csv';
import { saveBlob } from '../../../homework/utils/homework-format';
import { GridCode, MonthStatus, SectionFeeGrid, SectionFeeStatus, SectionFeeStudent } from '../../models/section-fee-status.model';
import { FeeReportService } from '../../services/fee-report.service';

const ALL = 'All';
const STATUSES: MonthStatus[] = ['Overdue', 'Unpaid', 'Partially paid', 'Not due yet', 'Paid', 'No fee this month'];
/** Words AND a mark with every status, so colour is never the only signal. */
export const STATUS_MARK: Record<MonthStatus, string> = { Paid: '✓', 'Partially paid': '◐', Unpaid: '○', Overdue: '!', 'Not due yet': '…', 'No fee this month': '–' };
const CODE_STATUS: Record<GridCode, MonthStatus> = { P: 'Paid', Partial: 'Partially paid', U: 'Unpaid', O: 'Overdue', N: 'Not due yet', '-': 'No fee this month' };
const CODE_MARK: Record<GridCode, string> = { P: '✓', Partial: '◐', U: '○', O: '!', N: '…', '-': '–' };

/**
 * "Fee Status" for a class teacher, MONTH by month: has THIS month's fee been submitted, and what is still owed from before. Read-only: a status and amounts per student, no receipts,
 * payment modes, concessions or discounts. A month = the instalments whose due date falls in it. The Year grid shows every month at a glance (status only, no amounts).
 * The sections come from the same call as Mark Attendance (the sections this teacher is class teacher of); the server re-checks which teacher on every request.
 */
@Component({
  selector: 'app-section-fee-status',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './section-fee-status.component.scss',
  templateUrl: './section-fee-status.component.html',
})
export class SectionFeeStatusComponent implements OnInit {
  private readonly attendance = inject(AttendanceService);
  private readonly fees = inject(FeeReportService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Students' }, { label: 'Fee Status' }];
  protected readonly inr = formatInr;
  protected readonly mark = STATUS_MARK;
  protected readonly codeStatus = (c: GridCode) => CODE_STATUS[c];
  protected readonly codeMark = (c: GridCode) => CODE_MARK[c];

  protected readonly sections = signal<MarkableSection[]>([]);
  protected readonly yearName = signal('');
  protected readonly loadingSections = signal(true);
  protected readonly sectionsFailed = signal(false);
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl(ALL, { nonNullable: true });
  protected readonly monthControl = new FormControl('', { nonNullable: true });
  protected readonly search = signal('');
  protected readonly status = signal(ALL);
  protected readonly view = signal<'month' | 'year'>('month');

  protected readonly data = signal<SectionFeeStatus | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly grid = signal<SectionFeeGrid | null>(null);
  protected readonly gridLoading = signal(false);
  protected readonly gridFailed = signal(false);

  protected readonly sectionOptions = computed<SelectOption[]>(() => this.sections().map((s) => ({ label: `${s.className} – ${s.sectionName}`, value: String(s.sectionId) })));
  protected readonly statusOptions: SelectOption[] = [{ label: 'All statuses', value: ALL }, ...STATUSES.map((s) => ({ label: s, value: s }))];
  protected readonly monthOptions = computed<SelectOption[]>(() => (this.data()?.months ?? this.grid()?.months ?? []).map((m) => ({ label: m.label + (m.isCurrent ? ' (this month)' : ''), value: m.month })));
  private readonly monthIndex = computed(() => (this.data()?.months ?? []).findIndex((m) => m.month === this.data()?.month));
  protected readonly canPrev = computed(() => this.monthIndex() > 0);
  protected readonly canNext = computed(() => this.monthIndex() >= 0 && this.monthIndex() < (this.data()?.months.length ?? 0) - 1);
  /** No fee structure for anyone in the class: say so instead of a table full of dashes. */
  protected readonly noStructure = computed(() => { const s = this.data()?.summary; return !!s && s.students > 0 && s.noFeeAssigned === s.students; });

  /** Students who still owe something first (Overdue, Unpaid, Partially paid), then everyone else; inside each, the highest balance first. Filtered by status and by name / roll number. */
  protected readonly rows = computed<SectionFeeStudent[]>(() => {
    const q = this.search().trim().toLowerCase();
    const st = this.status();
    const owes = (s: SectionFeeStudent) => (s.monthStatus === 'Overdue' || s.monthStatus === 'Unpaid' || s.monthStatus === 'Partially paid' ? 0 : 1);
    return (this.data()?.students ?? [])
      .filter((s) => (st === ALL || s.monthStatus === st) && (!q || s.fullName.toLowerCase().includes(q) || (s.rollNumber ?? '').toLowerCase().includes(q)))
      .sort((a, b) => owes(a) - owes(b) || b.balanceThisMonth - a.balanceThisMonth || b.totalOutstandingTillDate - a.totalOutstandingTillDate || a.fullName.localeCompare(b.fullName));
  });

  protected readonly tone = (status: string) =>
    ({ Paid: 'green', 'Partially paid': 'yellow', Overdue: 'red', Unpaid: 'orange', 'Not due yet': 'blue' } as Record<string, string>)[status] ?? 'gray';

  ngOnInit(): void {
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.grid.set(null);
      this.loadStatus(true);
      if (this.view() === 'year') this.loadGrid();
    });
    this.monthControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.loadStatus(false));
    this.statusControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.status.set(v));
    this.loadSections();
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected loadSections(): void {
    this.loadingSections.set(true);
    this.sectionsFailed.set(false);
    this.attendance
      .mySections()
      .pipe(finalize(() => this.loadingSections.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (m) => {
          this.sections.set(m.sections);
          this.yearName.set(m.academicYearName);
          if (m.sections.length) this.sectionControl.setValue(String(m.sections[0].sectionId));
        },
        error: () => this.sectionsFailed.set(true), // already toasted by the interceptor
      });
  }

  /** `useDefault` = ask the server for its default month (the current one, kept inside the year); otherwise the month in the picker. */
  protected loadStatus(useDefault: boolean): void {
    const id = Number(this.sectionControl.value);
    if (!id) { this.data.set(null); return; }
    this.loading.set(true);
    this.loadFailed.set(false);
    this.fees
      .sectionStatus(id, undefined, useDefault ? undefined : this.monthControl.value || undefined)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => { this.data.set(d); this.monthControl.setValue(d.month, { emitEvent: false }); },
        error: () => { this.data.set(null); this.loadFailed.set(true); },
      });
  }

  protected step(delta: number): void {
    const months = this.data()?.months ?? [];
    const next = months[this.monthIndex() + delta];
    if (next) this.monthControl.setValue(next.month);
  }

  protected setView(view: 'month' | 'year'): void {
    this.view.set(view);
    if (view === 'year' && !this.grid() && !this.gridLoading()) this.loadGrid();
  }

  protected loadGrid(): void {
    const id = Number(this.sectionControl.value);
    if (!id) return;
    this.gridLoading.set(true);
    this.gridFailed.set(false);
    this.fees
      .sectionStatusMonths(id)
      .pipe(finalize(() => this.gridLoading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (g) => this.grid.set(g), error: () => this.gridFailed.set(true) });
  }

  /** What is on screen (the selected month, the filter and the order) as CSV; built here, nothing is sent anywhere. */
  protected exportCsv(): void {
    const d = this.data();
    if (!d) return;
    const lines = [
      ['Roll', 'Student', 'Month', 'Status', 'Due this month', 'Paid this month', 'Balance this month', 'Total outstanding till date'],
      ...this.rows().map((s) => [s.rollNumber ?? '', s.fullName, d.monthLabel, s.monthStatus, s.dueThisMonth, s.paidThisMonth, s.balanceThisMonth, s.totalOutstandingTillDate]),
    ].map((r) => r.map(csvField).join(','));
    const safe = `${d.className}-${d.sectionName}`.replace(/[^A-Za-z0-9]+/g, '-').replace(/^-|-$/g, '');
    saveBlob(new Blob(['﻿', lines.join('\r\n') + '\r\n'], { type: 'text/csv;charset=utf-8' }), `fee-status-${safe}-${d.month}.csv`);
  }
}
