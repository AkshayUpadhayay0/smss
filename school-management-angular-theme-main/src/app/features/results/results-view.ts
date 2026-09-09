import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { BarChartComponent } from '../../shared/components/charts/bar-chart';
import { ExamService } from '../../core/services/data.service';
import { Exam, MarksEntry } from '../../core/models/school.models';

@Component({
  selector: 'app-results-view',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, StatusBadgeComponent, BarChartComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './results-view.html',
})
export class ResultsViewComponent {
  private examService = inject(ExamService);
  exams = signal<Exam[]>([]);
  selectedExam = signal('');
  entries = signal<MarksEntry[]>([]);

  constructor() {
    this.examService.getAll().subscribe(list => {
      this.exams.set(list);
      if (list[0]) { this.selectedExam.set(list[0].id); this.load(); }
    });
  }

  load(): void {
    const exam = this.exams().find(e => e.id === this.selectedExam());
    if (!exam) return;
    this.examService.getMarks(exam.classId, exam.subjects.map(s => s.subject)).subscribe(list =>
      this.entries.set(list.sort((a, b) => b.percentage - a.percentage))
    );
  }

  passCount = computed(() => this.entries().filter(e => e.result === 'Pass').length);
  failCount = computed(() => this.entries().filter(e => e.result === 'Fail').length);
  avgPct = computed(() => {
    const list = this.entries();
    return list.length ? Math.round((list.reduce((a, e) => a + e.percentage, 0) / list.length) * 10) / 10 : 0;
  });
  topper = computed(() => this.entries()[0]);

  gradeDistribution = computed(() => {
    const grades = ['A+', 'A', 'B+', 'B', 'C', 'D', 'F'];
    const counts = grades.map(g => this.entries().filter(e => e.grade === g).length);
    return {
      labels: grades,
      series: [{ name: 'Students', color: 'var(--primary)', values: counts }],
    };
  });
}
