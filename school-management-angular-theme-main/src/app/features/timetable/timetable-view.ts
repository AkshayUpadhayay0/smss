import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { TimetableService, ClassService, TeacherService } from '../../core/services/data.service';
import { SchoolClass, Teacher, TimetablePeriod } from '../../core/models/school.models';

const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'] as const;
const SLOTS = [
  ['08:00', '08:45'], ['08:45', '09:30'], ['09:30', '10:15'], ['10:15', '10:30'],
  ['10:30', '11:15'], ['11:15', '12:00'], ['12:00', '12:45'], ['12:45', '13:30'],
];

@Component({
  selector: 'app-timetable-view',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, TabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './timetable-view.html',
})
export class TimetableViewComponent {
  private timetableService = inject(TimetableService);
  private classService = inject(ClassService);
  private teacherService = inject(TeacherService);

  classes = signal<SchoolClass[]>([]);
  teachers = signal<Teacher[]>([]);
  periods = signal<TimetablePeriod[]>([]);
  view = signal<'class' | 'teacher' | 'room'>('class');
  selectedClass = signal('cls-9');
  selectedSection = signal('sec-9-0');
  selectedTeacher = signal('');
  days = DAYS;
  slots = SLOTS;

  tabs: TabItem[] = [
    { id: 'class', label: 'Class Timetable', icon: 'layers' },
    { id: 'teacher', label: 'Teacher Timetable', icon: 'user-check' },
    { id: 'room', label: 'Room Timetable', icon: 'grid' },
  ];

  constructor() {
    this.classService.getAll().subscribe(list => this.classes.set(list));
    this.teacherService.getAll().subscribe(list => { this.teachers.set(list); this.selectedTeacher.set(list[0]?.id ?? ''); });
    this.load();
  }

  availableSections = computed(() => this.classes().find(c => c.id === this.selectedClass())?.sections ?? []);

  load(): void {
    this.timetableService.getTimetable(this.selectedClass(), this.selectedSection()).subscribe(tt => this.periods.set(tt.periods));
  }

  cellFor(day: string, start: string): TimetablePeriod | undefined {
    return this.periods().find(p => p.day === day && p.startTime === start);
  }

  setView(value: string): void {
    this.view.set(value === 'teacher' ? 'teacher' : value === 'room' ? 'room' : 'class');
  }
}
