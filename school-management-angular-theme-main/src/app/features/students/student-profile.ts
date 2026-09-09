import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { ProgressBarComponent } from '../../shared/components/progress-bar/progress-bar';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { StudentService } from '../../core/services/data.service';
import { Student } from '../../core/models/school.models';
import * as M from '../../core/services/mock-data';

@Component({
  selector: 'app-student-profile',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent, IconComponent, AvatarComponent, StatusBadgeComponent, ProgressBarComponent, TabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './student-profile.html',
})
export class StudentProfileComponent {
  private route = inject(ActivatedRoute);
  private studentService = inject(StudentService);

  student = signal<Student | undefined>(undefined);
  activeTab = signal('overview');

  tabs: TabItem[] = [
    { id: 'overview', label: 'Overview', icon: 'user' },
    { id: 'attendance', label: 'Attendance', icon: 'check-square' },
    { id: 'fees', label: 'Fees', icon: 'wallet' },
    { id: 'homework', label: 'Homework', icon: 'clipboard-list' },
    { id: 'exams', label: 'Exams', icon: 'award' },
    { id: 'results', label: 'Results', icon: 'bar-chart' },
    { id: 'documents', label: 'Documents', icon: 'file-text' },
  ];

  recentAttendance = M.generateAttendanceForDate(new Date().toISOString().slice(0, 10)).slice(0, 10);
  homework = M.HOMEWORK.slice(0, 6);
  exams = M.EXAMS;

  constructor() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) this.studentService.getById(id).subscribe(s => this.student.set(s));
  }

  studentFee(id: string) {
    return M.STUDENT_FEES.find(f => f.studentId === id) ?? M.STUDENT_FEES[0];
  }
}
