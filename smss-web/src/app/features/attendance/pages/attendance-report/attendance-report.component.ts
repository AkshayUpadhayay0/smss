import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { saveBlob } from '../../../homework/utils/homework-format';
import { formatDate } from '../../../admissions/models/admission.model';
import { MonthlyAttendanceReport, ReportMark, ReportOptions, ReportSectionOption } from '../../models/attendance-report.model';
import { AttendanceService } from '../../services/attendance.service';
import { reportFileName, reportToCsv } from '../../utils/attendance-report-csv';

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const WEEKDAY = ['M', 'T', 'W', 'T', 'F', 'S', 'S'];

/** The months an academic year spans, as "yyyy-MM" values. */
function monthsOf(start: string, end: string): { value: string; label: string }[] {
  const out: { value: string; label: string }[] = [];
  let y = Number(start.slice(0, 4));
  let m = Number(start.slice(5, 7));
  const endKey = Number(end.slice(0, 4)) * 12 + Number(end.slice(5, 7));
  while (y * 12 + m <= endKey && out.length < 24) {
    out.push({ value: `${y}-${String(m).padStart(2, '0')}`, label: `${MONTHS[m - 1]} ${y}` });
    if (++m > 12) { m = 1; y++; }
  }
  return out;
}

/**
 * Monthly attendance report: who was present / absent on each day of a month, per student, with totals and a class summary.
 * School Admin and Principal pick any class and section (read-only); a class teacher gets their own section(s). Export: CSV (built here) and a PDF (built by the server).
 */
@Component({
  selector: 'app-attendance-report',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './attendance-report.component.scss',
  templateUrl: './attendance-report.component.html',
})
export class AttendanceReportComponent implements OnInit {
  private readonly service = inject(AttendanceService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Attendance' }, { label: 'Attendance Report' }];
  protected readonly fmt = formatDate;
  protected readonly weekday = (dayOfWeek: number) => WEEKDAY[dayOfWeek - 1];

  protected readonly options = signal<ReportOptions | null>(null);
  protected readonly loadingOptions = signal(true);
  protected readonly optionsFailed = signal(false);

  protected readonly yearControl = new FormControl('', { nonNullable: true });
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  protected readonly monthControl = new FormControl('', { nonNullable: true });

  protected readonly report = signal<MonthlyAttendanceReport | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly exporting = signal(false);

  private readonly classId = signal('');
  private readonly yearId = signal('');

  protected readonly yearOptions = computed<SelectOption[]>(() => (this.options()?.years ?? []).map((y) => ({ label: y.yearName + (y.isCurrent ? ' (current)' : ''), value: String(y.academicYearId) })));
  protected readonly allSections = computed(() => this.options()?.allSections ?? false);
  protected readonly classOptions = computed<SelectOption[]>(() => {
    const seen = new Map<number, string>();
    for (const s of this.options()?.sections ?? []) if (!seen.has(s.classId)) seen.set(s.classId, s.className);
    return [...seen].map(([id, name]) => ({ label: name, value: String(id) }));
  });
  /** A School Admin / Principal picks a class, then one of ITS sections; a class teacher picks among their own sections ("Class – Section"). */
  protected readonly sectionOptions = computed<SelectOption[]>(() => {
    const all = this.allSections();
    const classId = this.classId();
    return (this.options()?.sections ?? [])
      .filter((s) => !all || String(s.classId) === classId)
      .map((s: ReportSectionOption) => ({ label: all ? s.sectionName : `${s.className} – ${s.sectionName}`, value: String(s.sectionId) }));
  });
  protected readonly monthOptions = computed<SelectOption[]>(() => {
    const y = (this.options()?.years ?? []).find((x) => String(x.academicYearId) === this.yearId());
    return y ? monthsOf(y.startDate, y.endDate) : [];
  });
  protected readonly noSections = computed(() => !this.loadingOptions() && !this.optionsFailed() && (this.options()?.sections.length ?? 0) === 0);
  protected readonly yearName = computed(() => (this.options()?.years ?? []).find((y) => String(y.academicYearId) === this.yearId())?.yearName ?? '');

  ngOnInit(): void {
    this.yearControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.onYear(v));
    this.classControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.classId.set(v);
      const first = this.sectionOptions()[0];
      this.sectionControl.setValue(first?.value ?? '');
    });
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.loadReport());
    this.monthControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.loadReport());
    this.loadOptions();
  }

  protected loadOptions(academicYearId?: number): void {
    this.loadingOptions.set(true);
    this.optionsFailed.set(false);
    this.service
      .reportOptions(academicYearId)
      .pipe(finalize(() => this.loadingOptions.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (o) => this.applyOptions(o, academicYearId !== undefined),
        error: () => this.optionsFailed.set(true), // already toasted by the interceptor
      });
  }

  /** First load picks the current year, today's month (or the year's first month) and the first section; a year change keeps the chosen section when it still exists. */
  private applyOptions(o: ReportOptions, keepChoice: boolean): void {
    const previousSection = this.sectionControl.value;
    this.options.set(o);
    this.yearId.set(String(o.academicYearId));
    this.yearControl.setValue(String(o.academicYearId), { emitEvent: false });

    const months = this.monthOptions();
    const today = new Date();
    const key = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`;
    const chosenMonth = keepChoice && months.some((m) => m.value === this.monthControl.value) ? this.monthControl.value : months.some((m) => m.value === key) ? key : (months[0]?.value ?? '');
    this.monthControl.setValue(chosenMonth, { emitEvent: false });

    const sections = o.sections;
    const keep = keepChoice ? sections.find((s) => String(s.sectionId) === previousSection) : undefined;
    const section = keep ?? sections[0];
    this.classId.set(section ? String(section.classId) : '');
    this.classControl.setValue(section ? String(section.classId) : '', { emitEvent: false });
    this.sectionControl.setValue(section ? String(section.sectionId) : '', { emitEvent: false });
    this.loadReport();
  }

  private onYear(value: string): void {
    this.report.set(null);
    if (value) this.loadOptions(Number(value));
  }

  protected loadReport(): void {
    const sectionId = Number(this.sectionControl.value);
    const [y, m] = this.monthControl.value.split('-').map(Number);
    if (!sectionId || !y || !m) {
      this.report.set(null);
      return;
    }
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .monthlyReport(sectionId, y, m, Number(this.yearControl.value) || undefined)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => this.report.set(r),
        error: () => {
          this.report.set(null);
          this.loadFailed.set(true); // already toasted by the interceptor
        },
      });
  }

  protected cell(mark: ReportMark, dayIndex: number): { text: string; cls: string } {
    const day = this.report()?.days[dayIndex];
    switch (mark) {
      case 'Present': return { text: 'P', cls: 'p' };
      case 'Absent': return { text: 'A', cls: 'a' };
      case 'NotMarked': return day?.isFuture || !day?.isWorkingDay ? { text: '', cls: '' } : { text: '–', cls: 'nm' };
      default: return { text: '', cls: '' };
    }
  }

  protected dayTitle(d: { date: string; nonWorkingReason?: string | null; holidayName?: string | null; markedCount: number; isWorkingDay: boolean; isFuture: boolean }): string {
    const base = formatDate(d.date);
    if (d.holidayName) return `${base}: ${d.holidayName}`;
    if (d.nonWorkingReason) return `${base}: ${d.nonWorkingReason}`;
    return d.isFuture ? base : d.markedCount === 0 ? `${base}: attendance was not marked` : base;
  }

  protected exportCsv(): void {
    const r = this.report();
    if (!r) return;
    // a BOM so Excel reads the file as UTF-8 (names in Devanagari etc.)
    saveBlob(new Blob(['﻿', reportToCsv(r)], { type: 'text/csv;charset=utf-8' }), reportFileName(r, 'csv'));
  }

  protected exportPdf(): void {
    const r = this.report();
    if (!r || this.exporting()) return;
    this.exporting.set(true);
    this.service
      .monthlyReportPdf(r.sectionId, r.year, r.month, r.academicYearId)
      .pipe(finalize(() => this.exporting.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => saveBlob(blob, reportFileName(r, 'pdf')),
        error: (e: Error) => this.toast.error(e.message),
      });
  }
}
