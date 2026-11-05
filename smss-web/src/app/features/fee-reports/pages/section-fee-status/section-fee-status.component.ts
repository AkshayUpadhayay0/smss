import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { csvField } from '../../../attendance/utils/attendance-report-csv';
import { AttendanceService } from '../../../attendance/services/attendance.service';
import { MarkableSection } from '../../../attendance/models/attendance.model';
import { saveBlob } from '../../../homework/utils/homework-format';
import { SectionFeeStatus, SectionFeeStudent } from '../../models/section-fee-status.model';
import { FeeReportService } from '../../services/fee-report.service';

const ALL = 'All';
const STATUSES = ['Overdue', 'Partially Paid', 'Pending', 'Paid', 'Not Set'];

/**
 * "Fee Status" for a class teacher: how their class stands on fees (a status and three amounts per student). Read-only: no receipts, payment modes,
 * concessions or discounts, and nothing to collect. The sections come from the same call as Mark Attendance (the sections this teacher is class teacher of);
 * the server re-checks which teacher on every request.
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

  protected readonly sections = signal<MarkableSection[]>([]);
  protected readonly yearName = signal('');
  protected readonly loadingSections = signal(true);
  protected readonly sectionsFailed = signal(false);
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl(ALL, { nonNullable: true });
  protected readonly search = signal('');
  protected readonly status = signal(ALL);

  protected readonly data = signal<SectionFeeStatus | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);

  protected readonly sectionOptions = computed<SelectOption[]>(() => this.sections().map((s) => ({ label: `${s.className} – ${s.sectionName}`, value: String(s.sectionId) })));
  protected readonly statusOptions: SelectOption[] = [{ label: 'All statuses', value: ALL }, ...STATUSES.map((s) => ({ label: s, value: s }))];

  /** Highest balance first (who owes the most), then by name; filtered by status and by the search text (name or roll number). */
  protected readonly rows = computed<SectionFeeStudent[]>(() => {
    const q = this.search().trim().toLowerCase();
    const st = this.status();
    return (this.data()?.students ?? [])
      .filter((s) => (st === ALL || s.status === st) && (!q || s.fullName.toLowerCase().includes(q) || (s.rollNumber ?? '').toLowerCase().includes(q)))
      .sort((a, b) => b.balance - a.balance || a.fullName.localeCompare(b.fullName));
  });

  protected readonly tone = (status: string) => ({ Paid: 'green', 'Partially Paid': 'yellow', Pending: 'gray', Overdue: 'red' } as Record<string, string>)[status] ?? 'gray';

  ngOnInit(): void {
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.loadStatus());
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

  protected loadStatus(): void {
    const id = Number(this.sectionControl.value);
    if (!id) { this.data.set(null); return; }
    this.loading.set(true);
    this.loadFailed.set(false);
    this.fees
      .sectionStatus(id)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => this.data.set(d),
        error: () => { this.data.set(null); this.loadFailed.set(true); },
      });
  }

  /** What is on screen (the current filter and order) as CSV; built here, nothing is sent anywhere. */
  protected exportCsv(): void {
    const d = this.data();
    if (!d) return;
    const lines = [['Roll', 'Student', 'Status', 'Total due', 'Paid', 'Balance'], ...this.rows().map((s) => [s.rollNumber ?? '', s.fullName, s.status, s.totalDue, s.totalPaid, s.balance])]
      .map((r) => r.map(csvField).join(','));
    const safe = `${d.className}-${d.sectionName}`.replace(/[^A-Za-z0-9]+/g, '-').replace(/^-|-$/g, '');
    saveBlob(new Blob(['﻿', lines.join('\r\n') + '\r\n'], { type: 'text/csv;charset=utf-8' }), `fee-status-${safe}.csv`);
  }
}
