import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatCardComponent } from '../../shared/components/stat-card/stat-card';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { ProgressBarComponent } from '../../shared/components/progress-bar/progress-bar';
import { DonutChartComponent } from '../../shared/components/charts/donut-chart';
import { BarChartComponent } from '../../shared/components/charts/bar-chart';
import { LineChartComponent } from '../../shared/components/charts/line-chart';
import { BrandingService } from '../../core/services/branding.service';
import { AuthService } from '../../core/services/auth.service';
import * as M from '../../core/services/mock-data';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule, RouterLink, IconComponent, StatCardComponent, AvatarComponent,
    ProgressBarComponent, DonutChartComponent, BarChartComponent, LineChartComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardComponent {
  branding = inject(BrandingService);
  auth = inject(AuthService);

  session = M.currentSessionLabel();
  totalStudents = M.STUDENTS.length;
  totalTeachers = M.TEACHERS.length;
  totalClasses = M.CLASSES.length;

  studentStats = computed(() => {
    const boys = M.STUDENTS.filter(s => s.gender === 'Male').length;
    const girls = M.STUDENTS.filter(s => s.gender === 'Female').length;
    const newAdmissions = M.ADMISSIONS.filter(a => a.status === 'Approved').length;
    const inactive = M.STUDENTS.filter(s => s.status === 'Inactive').length;
    return { boys, girls, newAdmissions, inactive };
  });

  attendanceToday = computed(() => {
    const records = M.generateAttendanceForDate(new Date().toISOString().slice(0, 10));
    const present = records.filter(r => r.status === 'Present').length;
    const absent = records.filter(r => r.status === 'Absent').length;
    const late = records.filter(r => r.status === 'Late').length;
    const leave = records.filter(r => r.status === 'Leave').length;
    const total = records.length || 1;
    return { present, absent, late, leave, pct: Math.round((present / total) * 100) };
  });

  attendanceDonut = computed(() => {
    const a = this.attendanceToday();
    return [
      { label: 'Present', value: a.present, color: 'var(--success)' },
      { label: 'Absent', value: a.absent, color: 'var(--danger)' },
      { label: 'Late', value: a.late, color: 'var(--warning)' },
      { label: 'Leave', value: a.leave, color: 'var(--info)' },
    ];
  });

  genderDonut = computed(() => {
    const s = this.studentStats();
    return [
      { label: 'Boys', value: s.boys, color: 'var(--primary)' },
      { label: 'Girls', value: s.girls, color: '#ec4899' },
    ];
  });

  feeStats = computed(() => {
    const fees = M.STUDENT_FEES;
    const total = fees.reduce((a, f) => a + f.totalFee, 0);
    const collected = fees.reduce((a, f) => a + f.paid, 0);
    const pending = total - collected;
    const overdue = fees.filter(f => f.status === 'Overdue').reduce((a, f) => a + f.pending, 0);
    return { total, collected, pending, overdue, pct: Math.round((collected / total) * 100) };
  });

  homeworkStats = computed(() => {
    const hw = M.HOMEWORK;
    return {
      assigned: hw.length,
      submitted: hw.filter(h => h.status === 'Submitted' || h.status === 'Graded').length,
      pending: hw.filter(h => h.status === 'Pending').length,
      overdue: hw.filter(h => h.status === 'Overdue').length,
    };
  });

  teacherStats = computed(() => {
    const att = M.TEACHER_ATTENDANCE_TODAY;
    return {
      total: M.TEACHERS.length,
      present: att.filter(a => a.status === 'Present').length,
      absent: att.filter(a => a.status === 'Absent').length,
      leave: att.filter(a => a.status === 'Leave').length,
    };
  });

  performanceBar = {
    labels: M.CLASSES.slice(4, 10).map(c => c.name.replace('Class ', '')),
    series: [
      { name: 'Average %', color: 'var(--primary)', values: [72, 78, 81, 76, 84, 88] },
    ],
  };

  admissionTrend = {
    labels: ['Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep'],
    series: [
      { name: 'New Admissions', color: 'var(--success)', values: [32, 48, 65, 40, 55, 61] },
      { name: 'Withdrawals', color: 'var(--danger)', values: [4, 6, 3, 8, 5, 2] },
    ],
  };

  upcomingEvents = M.SCHOOL_EVENTS.slice(0, 5);
  recentNotices = M.NOTICES.filter(n => n.status === 'Published').slice(0, 4);
  recentActivities = M.ACTIVITIES;
  todaysTimetable = M.generateTimetable('cls-9', 'sec-9-0').periods.filter(p => p.day === 'Mon');

  admissionsPending = M.ADMISSIONS.filter(a => a.status === 'Pending' || a.status === 'Under Review').length;
  upcomingExamsCount = M.EXAMS.filter(e => e.status === 'Upcoming').length;

  greeting = signal(this.getGreeting());

  private getGreeting(): string {
    const hour = new Date().getHours();
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    return 'Good evening';
  }
}
