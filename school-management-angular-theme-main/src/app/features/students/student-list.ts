import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { PaginatorComponent } from '../../shared/components/paginator/paginator';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state';
import { StudentService, ClassService } from '../../core/services/data.service';
import { Student, SchoolClass } from '../../core/models/school.models';

@Component({
  selector: 'app-student-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PageHeaderComponent, IconComponent, AvatarComponent, StatusBadgeComponent, PaginatorComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './student-list.html',
})
export class StudentListComponent {
  private studentService = inject(StudentService);
  private classService = inject(ClassService);
  private router = inject(Router);

  students = signal<Student[]>([]);
  classes = signal<SchoolClass[]>([]);
  loading = signal(true);

  query = signal('');
  classFilter = signal('all');
  sectionFilter = signal('all');
  statusFilter = signal('all');
  sortBy = signal<'name' | 'class' | 'attendance'>('name');
  page = signal(1);
  pageSize = 10;
  selectedIds = signal<Set<string>>(new Set());

  constructor() {
    this.studentService.getAll().subscribe(list => { this.students.set(list); this.loading.set(false); });
    this.classService.getAll().subscribe(list => this.classes.set(list));
  }

  availableSections = computed(() => {
    const cls = this.classes().find(c => c.id === this.classFilter());
    return cls ? cls.sections : [];
  });

  filtered = computed(() => {
    let list = this.students();
    const q = this.query().trim().toLowerCase();
    if (q) {
      list = list.filter(s =>
        `${s.firstName} ${s.lastName}`.toLowerCase().includes(q) ||
        s.admissionNo.toLowerCase().includes(q) ||
        s.guardian.phone.includes(q)
      );
    }
    if (this.classFilter() !== 'all') list = list.filter(s => s.classId === this.classFilter());
    if (this.sectionFilter() !== 'all') list = list.filter(s => s.sectionId === this.sectionFilter());
    if (this.statusFilter() !== 'all') list = list.filter(s => s.status === this.statusFilter());

    const sorted = [...list];
    if (this.sortBy() === 'name') sorted.sort((a, b) => a.firstName.localeCompare(b.firstName));
    if (this.sortBy() === 'class') sorted.sort((a, b) => a.className.localeCompare(b.className));
    if (this.sortBy() === 'attendance') sorted.sort((a, b) => b.attendancePercent - a.attendancePercent);
    return sorted;
  });

  paged = computed(() => {
    const start = (this.page() - 1) * this.pageSize;
    return this.filtered().slice(start, start + this.pageSize);
  });

  totalCount = computed(() => this.filtered().length);

  onFilterChange(): void {
    this.page.set(1);
  }

  toggleSelect(id: string): void {
    const next = new Set(this.selectedIds());
    if (next.has(id)) next.delete(id); else next.add(id);
    this.selectedIds.set(next);
  }

  toggleSelectAll(): void {
    const pageIds = this.paged().map(s => s.id);
    const allSelected = pageIds.every(id => this.selectedIds().has(id));
    const next = new Set(this.selectedIds());
    if (allSelected) pageIds.forEach(id => next.delete(id));
    else pageIds.forEach(id => next.add(id));
    this.selectedIds.set(next);
  }

  isPageAllSelected(): boolean {
    const pageIds = this.paged().map(s => s.id);
    return pageIds.length > 0 && pageIds.every(id => this.selectedIds().has(id));
  }

  clearSelection(): void {
    this.selectedIds.set(new Set());
  }

  openStudent(id: string): void {
    this.router.navigate(['/students', id]);
  }
}
