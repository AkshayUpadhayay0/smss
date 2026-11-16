import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom, finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { ToastService } from '../../../../core/services/toast.service';
import { concessionNameWithValue } from '../../../fee-setup/models/fee-concession.model';
import { ConcessionGivenRow, csvCell } from '../../models/fee-report.model';
import { FeeReportService } from '../../services/fee-report.service';

interface GivenTableRow extends Record<string, unknown> {
  paymentDate: string;
  receiptNumber: string;
  student: string;
  concession: string;
  amount: string;
  reason: string;
  appliedBy: string;
  status: string;
}

const COLUMNS: TableColumn<GivenTableRow>[] = [
  { key: 'paymentDate', label: 'Date' },
  { key: 'receiptNumber', label: 'Receipt' },
  { key: 'student', label: 'Student' },
  { key: 'concession', label: 'Concession' },
  { key: 'amount', label: 'Amount' },
  { key: 'reason', label: 'Reason' },
  { key: 'appliedBy', label: 'Applied by' },
  { key: 'status', label: 'Status', type: 'badge', tone: (v) => (v === 'Voided' ? 'red' : 'green') },
];

/** Every export is capped so a very busy school cannot freeze the browser: 100 rows per request, at most this many rows. */
const EXPORT_CAP = 10000;
const EXPORT_PAGE = 100;

const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const when = (value: string) => new Date(value).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });

/**
 * Concessions given at payment time: date, student, receipt, concession, amount, reason and who applied it. Read-only for School Admin, Accountant and Principal;
 * filterable by date range, paginated by the server, with a CSV export built in the browser. Voided payments stay in the list (the audit trail) but not in the total.
 */
@Component({
  selector: 'app-concessions-given',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './concessions-given.component.scss',
  templateUrl: './concessions-given.component.html',
})
export class ConcessionsGivenComponent implements OnInit {
  private readonly reports = inject(FeeReportService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Fees' }, { label: 'Concessions given' }];
  protected readonly columns = COLUMNS;
  protected readonly inr = formatInr;

  private readonly today = new Date();
  protected readonly fromControl = new FormControl(iso(new Date(this.today.getFullYear(), this.today.getMonth(), 1)), { nonNullable: true });
  protected readonly toControl = new FormControl(iso(this.today), { nonNullable: true });

  protected readonly items = signal<ConcessionGivenRow[]>([]);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly total = signal(0);
  protected readonly totalAmount = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly exporting = signal(false);

  protected readonly rangeError = computed(() => (this.range().from && this.range().to && this.range().to < this.range().from ? 'The end date can\'t be before the start date' : undefined));
  private readonly range = signal({ from: this.fromControl.value, to: this.toControl.value });

  protected readonly rows = computed<GivenTableRow[]>(() =>
    this.items().map((r) => ({
      paymentDate: when(r.paymentDate),
      receiptNumber: r.receiptNumber,
      student: `${r.studentName} (${r.studentId})`,
      concession: concessionNameWithValue(r.concessionName, r.concessionType, r.concessionValue),
      amount: formatInr(r.concessionAmount),
      reason: r.reason ?? '—',
      appliedBy: r.appliedByName ?? '—',
      status: r.status,
    })),
  );

  ngOnInit(): void {
    this.fromControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.range.update((r) => ({ ...r, from: v })));
    this.toControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.range.update((r) => ({ ...r, to: v })));
    this.load();
  }

  /** Apply the date range from the first page. */
  protected apply(): void {
    if (this.rangeError()) return;
    this.page.set(1);
    this.load();
  }

  protected clearDates(): void {
    this.fromControl.setValue('');
    this.toControl.setValue('');
    this.apply();
  }

  protected onPage(p: number): void {
    this.page.set(p);
    this.load();
  }

  protected onPageSize(n: number): void {
    this.pageSize.set(n);
    this.page.set(1);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.reports
      .concessionsGiven(this.fromControl.value || null, this.toControl.value || null, this.page(), this.pageSize())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.items.set(r.items);
          this.total.set(r.totalItems);
          this.totalAmount.set(r.totalAmount);
        },
        error: () => {
          this.items.set([]);
          this.total.set(0);
          this.totalAmount.set(0);
          this.failed.set(true);
        },
      });
  }

  /** Every row of the current date range (not only this page), built in the browser. */
  protected async exportCsv(): Promise<void> {
    if (this.exporting() || this.total() === 0) return;
    this.exporting.set(true);
    try {
      const all: ConcessionGivenRow[] = [];
      for (let p = 1; all.length < Math.min(this.total(), EXPORT_CAP); p++) {
        const res = await firstValueFrom(this.reports.concessionsGiven(this.fromControl.value || null, this.toControl.value || null, p, EXPORT_PAGE));
        all.push(...res.items);
        if (res.items.length < EXPORT_PAGE) break;
      }
      const lines: string[][] = [['Date', 'Receipt', 'Student ID', 'Student', 'Concession', 'Type', 'Value', 'Amount', 'Reason', 'Applied by', 'Status']];
      for (const r of all) {
        lines.push([when(r.paymentDate), r.receiptNumber, r.studentId, r.studentName, r.concessionName, r.concessionType, String(r.concessionValue), String(r.concessionAmount), r.reason ?? '', r.appliedByName ?? '', r.status]);
      }
      const blob = new Blob(['﻿' + lines.map((l) => l.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `concessions-given-${this.fromControl.value || 'start'}-${this.toControl.value || 'today'}.csv`.replace(/[^\w.-]+/g, '_');
      a.click();
      URL.revokeObjectURL(url);
      if (this.total() > EXPORT_CAP) this.toast.info(`The export holds the newest ${EXPORT_CAP} rows. Narrow the dates to export the rest.`);
    } catch {
      // the interceptor already toasted the reason
    } finally {
      this.exporting.set(false);
    }
  }
}
