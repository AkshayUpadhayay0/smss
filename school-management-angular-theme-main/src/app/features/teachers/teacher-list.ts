import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { PaginatorComponent } from '../../shared/components/paginator/paginator';
import { TeacherService } from '../../core/services/data.service';
import { Teacher } from '../../core/models/school.models';

@Component({
  selector: 'app-teacher-list',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, AvatarComponent, StatusBadgeComponent, PaginatorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './teacher-list.html',
})
export class TeacherListComponent {
  private teacherService = inject(TeacherService);
  private router = inject(Router);

  teachers = signal<Teacher[]>([]);
  loading = signal(true);
  query = signal('');
  deptFilter = signal('all');
  page = signal(1);
  pageSize = 10;

  departments = ['Mathematics', 'Science', 'English', 'Social Studies', 'Hindi', 'Computer Science', 'Physical Education', 'Arts', 'Music', 'Administration'];

  constructor() {
    this.teacherService.getAll().subscribe(list => { this.teachers.set(list); this.loading.set(false); });
  }

  filtered = computed(() => {
    let list = this.teachers();
    const q = this.query().trim().toLowerCase();
    if (q) list = list.filter(t => `${t.firstName} ${t.lastName}`.toLowerCase().includes(q) || t.employeeId.toLowerCase().includes(q));
    if (this.deptFilter() !== 'all') list = list.filter(t => t.department === this.deptFilter());
    return list;
  });

  paged = computed(() => {
    const start = (this.page() - 1) * this.pageSize;
    return this.filtered().slice(start, start + this.pageSize);
  });

  openTeacher(id: string): void {
    this.router.navigate(['/teachers', id]);
  }
}
