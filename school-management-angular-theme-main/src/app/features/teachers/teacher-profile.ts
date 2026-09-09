import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { TeacherService } from '../../core/services/data.service';
import { Teacher } from '../../core/models/school.models';
import * as M from '../../core/services/mock-data';

@Component({
  selector: 'app-teacher-profile',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, AvatarComponent, StatusBadgeComponent, TabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './teacher-profile.html',
})
export class TeacherProfileComponent {
  private route = inject(ActivatedRoute);
  private teacherService = inject(TeacherService);

  teacher = signal<Teacher | undefined>(undefined);
  activeTab = signal('overview');
  tabs: TabItem[] = [
    { id: 'overview', label: 'Overview', icon: 'user' },
    { id: 'schedule', label: 'Schedule', icon: 'clock' },
    { id: 'attendance', label: 'Attendance', icon: 'check-square' },
  ];

  weeklySchedule = M.generateTimetable('cls-9', 'sec-9-0').periods.filter(p => !p.isBreak).slice(0, 8);

  constructor() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) this.teacherService.getById(id).subscribe(t => this.teacher.set(t));
  }
}
