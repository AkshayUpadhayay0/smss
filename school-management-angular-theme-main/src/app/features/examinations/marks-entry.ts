import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { ExamService, ClassService } from '../../core/services/data.service';
import { Exam, MarksEntry, SchoolClass } from '../../core/models/school.models';

@Component({
  selector: 'app-marks-entry',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './marks-entry.html',
})
export class MarksEntryComponent {
  private examService = inject(ExamService);
  private classService = inject(ClassService);

  exams = signal<Exam[]>([]);
  classes = signal<SchoolClass[]>([]);
  selectedExam = signal('');
  entries = signal<MarksEntry[]>([]);
  subjects = signal<string[]>([]);
  saved = signal(false);

  constructor() {
    this.examService.getAll().subscribe(list => {
      this.exams.set(list);
      if (list[0]) { this.selectedExam.set(list[0].id); this.load(); }
    });
    this.classService.getAll().subscribe(list => this.classes.set(list));
  }

  load(): void {
    const exam = this.exams().find(e => e.id === this.selectedExam());
    if (!exam) return;
    const subjectNames = exam.subjects.map(s => s.subject);
    this.subjects.set(subjectNames);
    this.examService.getMarks(exam.classId, subjectNames).subscribe(list => this.entries.set(list));
  }

  updateMark(studentId: string, subject: string, value: string): void {
    const num = Math.max(0, Math.min(100, Number(value) || 0));
    this.entries.update(list => list.map(e => {
      if (e.studentId !== studentId) return e;
      const marks = { ...e.marks, [subject]: num };
      const total = Object.values(marks).reduce((a, v) => a + v, 0);
      const max = this.subjects().length * 100;
      const percentage = Math.round((total / max) * 1000) / 10;
      const grade = percentage >= 90 ? 'A+' : percentage >= 80 ? 'A' : percentage >= 70 ? 'B+' : percentage >= 60 ? 'B' : percentage >= 50 ? 'C' : percentage >= 35 ? 'D' : 'F';
      return { ...e, marks, total, percentage, grade, result: percentage >= 35 ? 'Pass' : 'Fail' };
    }));
  }

  classAverage = computed(() => {
    const list = this.entries();
    if (!list.length) return 0;
    return Math.round((list.reduce((a, e) => a + e.percentage, 0) / list.length) * 10) / 10;
  });

  passCount = computed(() => this.entries().filter(e => e.result === 'Pass').length);

  save(): void {
    this.saved.set(true);
    setTimeout(() => this.saved.set(false), 2200);
  }
}
