import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { ExamService } from '../../core/services/data.service';
import { Exam } from '../../core/models/school.models';

@Component({
  selector: 'app-exam-list',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent, IconComponent, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './exam-list.html',
})
export class ExamListComponent {
  private examService = inject(ExamService);
  exams = signal<Exam[]>([]);
  examTypes = ['Unit Test', 'Mid Term', 'Final Examination', 'Pre-Board'];

  constructor() {
    this.examService.getAll().subscribe(list => this.exams.set(list));
  }
}
