import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { ProgressBarComponent } from '../../shared/components/progress-bar/progress-bar';
import * as M from '../../core/services/mock-data';

@Component({
  selector: 'app-parent-portal',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent, IconComponent, AvatarComponent, StatusBadgeComponent, ProgressBarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './parent-portal.html',
})
export class ParentPortalComponent {
  children = M.STUDENTS.slice(0, 2);
  activeChild = signal(this.children[0]);

  todaysTimetable = M.generateTimetable('cls-9', 'sec-9-0').periods.filter(p => p.day === 'Mon' && !p.isBreak).slice(0, 5);
  homework = M.HOMEWORK.slice(0, 4);
  events = M.SCHOOL_EVENTS.slice(0, 3);
  notices = M.NOTICES.slice(0, 3);

  selectChild(c: typeof this.children[0]): void {
    this.activeChild.set(c);
  }

  fee(id: string) {
    return M.STUDENT_FEES.find(f => f.studentId === id) ?? M.STUDENT_FEES[0];
  }
}
