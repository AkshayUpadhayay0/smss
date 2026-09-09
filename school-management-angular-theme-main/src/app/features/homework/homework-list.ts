import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { ProgressBarComponent } from '../../shared/components/progress-bar/progress-bar';
import { ModalComponent } from '../../shared/components/modal/modal';
import { PaginatorComponent } from '../../shared/components/paginator/paginator';
import { HomeworkService } from '../../core/services/data.service';
import { Homework } from '../../core/models/school.models';

@Component({
  selector: 'app-homework-list',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, StatusBadgeComponent, ProgressBarComponent, ModalComponent, PaginatorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './homework-list.html',
})
export class HomeworkListComponent {
  private homeworkService = inject(HomeworkService);
  homework = signal<Homework[]>([]);
  query = signal('');
  statusFilter = signal('all');
  modalOpen = signal(false);
  page = signal(1);
  pageSize = 8;

  constructor() {
    this.homeworkService.getAll().subscribe(list => this.homework.set(list));
  }

  stats = computed(() => {
    const hw = this.homework();
    return {
      assigned: hw.length,
      submitted: hw.filter(h => h.status === 'Submitted' || h.status === 'Graded').length,
      pending: hw.filter(h => h.status === 'Pending').length,
      overdue: hw.filter(h => h.status === 'Overdue').length,
    };
  });

  filtered = computed(() => {
    let list = this.homework();
    const q = this.query().trim().toLowerCase();
    if (q) list = list.filter(h => h.title.toLowerCase().includes(q) || h.subject.toLowerCase().includes(q));
    if (this.statusFilter() !== 'all') list = list.filter(h => h.status === this.statusFilter());
    return list;
  });

  paged = computed(() => {
    const start = (this.page() - 1) * this.pageSize;
    return this.filtered().slice(start, start + this.pageSize);
  });

  progressPct(h: Homework): number {
    return Math.round((h.submissions / h.totalStudents) * 100);
  }
}
