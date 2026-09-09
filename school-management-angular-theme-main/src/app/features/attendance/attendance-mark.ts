import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { AttendanceService, ClassService } from '../../core/services/data.service';
import { AttendanceRecord, AttendanceStatus, SchoolClass } from '../../core/models/school.models';

@Component({
  selector: 'app-attendance-mark',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PageHeaderComponent, IconComponent, AvatarComponent, TabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './attendance-mark.html',
})
export class AttendanceMarkComponent {
  private attendanceService = inject(AttendanceService);
  private classService = inject(ClassService);

  classes = signal<SchoolClass[]>([]);
  records = signal<AttendanceRecord[]>([]);
  selectedDate = signal(new Date().toISOString().slice(0, 10));
  selectedClass = signal('cls-9');
  selectedSection = signal('');
  mode = signal<'student' | 'teacher'>('student');

  tabs: TabItem[] = [
    { id: 'student', label: 'Student Attendance', icon: 'users' },
    { id: 'teacher', label: 'Teacher Attendance', icon: 'user-check' },
  ];

  statusOptions: AttendanceStatus[] = ['Present', 'Absent', 'Late', 'Leave'];

  constructor() {
    this.classService.getAll().subscribe(list => {
      this.classes.set(list);
      const cls = list.find(c => c.id === 'cls-9') ?? list[0];
      if (cls) { this.selectedClass.set(cls.id); this.selectedSection.set(cls.sections[0]?.id ?? ''); }
      this.load();
    });
  }

  availableSections = computed(() => this.classes().find(c => c.id === this.selectedClass())?.sections ?? []);

  summary = computed(() => {
    const list = this.records();
    return {
      present: list.filter(r => r.status === 'Present').length,
      absent: list.filter(r => r.status === 'Absent').length,
      late: list.filter(r => r.status === 'Late').length,
      leave: list.filter(r => r.status === 'Leave').length,
      total: list.length,
    };
  });

  load(): void {
    this.attendanceService.getForDate(this.selectedDate(), this.selectedClass(), this.selectedSection()).subscribe(list => this.records.set(list));
  }

  setStatus(id: string, status: AttendanceStatus): void {
    this.records.update(list => list.map(r => r.id === id ? { ...r, status } : r));
  }

  markAll(status: AttendanceStatus): void {
    this.records.update(list => list.map(r => ({ ...r, status })));
  }

  setMode(value: string): void {
    this.mode.set(value === 'teacher' ? 'teacher' : 'student');
  }
}
