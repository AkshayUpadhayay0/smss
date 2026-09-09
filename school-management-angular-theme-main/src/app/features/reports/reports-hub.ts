import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { BarChartComponent } from '../../shared/components/charts/bar-chart';
import { LineChartComponent } from '../../shared/components/charts/line-chart';

interface ReportCategory { title: string; icon: string; color: string; reports: string[]; }

@Component({
  selector: 'app-reports-hub',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, BarChartComponent, LineChartComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reports-hub.html',
})
export class ReportsHubComponent {
  categories: ReportCategory[] = [
    { title: 'Student Reports', icon: 'users', color: 'var(--primary)', reports: ['Student List', 'Class-wise Students', 'Gender Distribution', 'Admission Report'] },
    { title: 'Attendance Reports', icon: 'check-square', color: 'var(--success)', reports: ['Daily Report', 'Monthly Report', 'Academic Year Report', 'Class-wise Report'] },
    { title: 'Academic Reports', icon: 'book-open', color: '#7c3aed', reports: ['Marks Report', 'Grade Distribution', 'Performance Report', 'Subject-wise Performance'] },
    { title: 'Fee Reports', icon: 'wallet', color: 'var(--warning)', reports: ['Collection Report', 'Pending Fees', 'Overdue Fees', 'Class-wise Fee Report'] },
    { title: 'Examination Reports', icon: 'award', color: 'var(--danger)', reports: ['Results Summary', 'Pass / Fail Report', 'Grade Distribution'] },
    { title: 'Staff Reports', icon: 'user-check', color: 'var(--info)', reports: ['Staff Directory', 'Attendance Report', 'Department-wise Report'] },
  ];

  filtersOpen = signal<string | null>(null);

  revenueTrend = {
    labels: ['Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep'],
    series: [{ name: 'Collection (₹L)', color: 'var(--success)', values: [12.4, 15.1, 18.8, 14.2, 16.9, 19.5] }],
  };

  classStrength = {
    labels: ['6', '7', '8', '9', '10', '11', '12'],
    series: [{ name: 'Students', color: 'var(--primary)', values: [156, 148, 162, 171, 158, 132, 128] }],
  };

  toggleFilters(title: string): void {
    this.filtersOpen.set(this.filtersOpen() === title ? null : title);
  }
}
