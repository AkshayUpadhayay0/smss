import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { CardComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { day } from '../../../homework/utils/homework-format';
import { StudentResult } from '../../models/assessment.model';
import { AssessmentService } from '../../services/assessment.service';

interface ResultRow extends Record<string, unknown> {
  assessmentId: number;
  title: string;
  typeName: string;
  subjectName: string;
  date: string;
  result: string;
  absent: boolean;
}

const COLUMNS: TableColumn<ResultRow>[] = [
  { key: 'title', label: 'Test' },
  { key: 'typeName', label: 'Type' },
  { key: 'subjectName', label: 'Subject' },
  { key: 'date', label: 'Date' },
  { key: 'result', label: 'Marks' },
];

/** A student's own test results, most recent first. Read-only, and only ever their own marks: no class average, no rank. */
@Component({
  selector: 'app-my-results',
  imports: [CardComponent, PageHeaderComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="My Results" subtitle="Your marks in class tests, most recent first" icon="file-text" [breadcrumbs]="breadcrumbs" />

    <app-card [noPadding]="true">
      <app-table [columns]="columns" [rows]="rows()" [loading]="loading()" [emptyMessage]="loadFailed() ? 'Your results could not be loaded.' : 'No test results yet.'" />
    </app-card>
  `,
})
export class MyResultsComponent implements OnInit {
  private readonly service = inject(AssessmentService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'My Results' }];
  protected readonly items = signal<StudentResult[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** Sorted by test date, most recent first (the server sorts too). "Absent" replaces the marks when the student missed the test. */
  protected readonly rows = computed<ResultRow[]>(() =>
    [...this.items()]
      .sort((a, b) => b.testDate.localeCompare(a.testDate) || b.assessmentId - a.assessmentId)
      .map((r) => ({
        assessmentId: r.assessmentId, title: r.title, typeName: r.assessmentTypeName, subjectName: r.subjectName, date: day(r.testDate),
        result: r.isAbsent ? 'Absent' : `${r.obtainedMarks} / ${r.maxMarks}`, absent: r.isAbsent,
      })),
  );

  ngOnInit(): void {
    this.service
      .studentMy()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (list) => this.items.set(list), error: () => this.loadFailed.set(true) });
  }
}
