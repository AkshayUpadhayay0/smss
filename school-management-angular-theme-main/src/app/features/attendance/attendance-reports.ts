import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { LineChartComponent } from '../../shared/components/charts/line-chart';
import { BarChartComponent } from '../../shared/components/charts/bar-chart';
import { DonutChartComponent } from '../../shared/components/charts/donut-chart';
import { ClassService } from '../../core/services/data.service';
import { SchoolClass } from '../../core/models/school.models';
import * as M from '../../core/services/mock-data';

@Component({
  selector: 'app-attendance-reports',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, TabsComponent, LineChartComponent, BarChartComponent, DonutChartComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './attendance-reports.html',
})
export class AttendanceReportsComponent {
  private classService = inject(ClassService);
  classes = signal<SchoolClass[]>([]);
  activeReport = signal('daily');
  today = new Date().toISOString().slice(0, 10);

  tabs: TabItem[] = [
    { id: 'daily', label: 'Daily Report', icon: 'calendar' },
    { id: 'monthly', label: 'Monthly Report', icon: 'bar-chart' },
    { id: 'yearly', label: 'Academic Year Report', icon: 'trending-up' },
  ];

  trendLabels = ['Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov'];
  trendSeries = [{ name: 'Attendance %', color: 'var(--success)', values: [92, 90, 94, 88, 91, 93, 89, 95] }];

  classComparisonLabels = M.CLASSES.slice(3, 9).map(c => c.name.replace('Class ', ''));
  classComparisonSeries = [{ name: 'Attendance %', color: 'var(--primary)', values: [91, 88, 94, 90, 86, 92] }];

  monthlyStudents = M.STUDENTS.slice(0, 12).map(s => ({
    name: `${s.firstName} ${s.lastName}`,
    workingDays: 24,
    present: Math.round((s.attendancePercent / 100) * 24),
    absent: 24 - Math.round((s.attendancePercent / 100) * 24),
    pct: s.attendancePercent,
  }));

  overallDonut = [
    { label: 'Present', value: 2180, color: 'var(--success)' },
    { label: 'Absent', value: 178, color: 'var(--danger)' },
    { label: 'Late', value: 66, color: 'var(--warning)' },
    { label: 'Leave', value: 34, color: 'var(--info)' },
  ];

  constructor() {
    this.classService.getAll().subscribe(list => this.classes.set(list));
  }
}
