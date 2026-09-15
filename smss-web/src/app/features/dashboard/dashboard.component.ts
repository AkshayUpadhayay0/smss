import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../shared/components/card/card.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { BadgeComponent } from '../../shared/components/badge/badge.component';
import { AvatarComponent } from '../../shared/components/avatar/avatar.component';
import { ButtonComponent } from '../../shared/components/button/button.component';
import {
  DASHBOARD_STATS,
  QUICK_ACTIONS,
  RECENT_ACTIVITY,
  RECENT_USERS,
  REVENUE_SERIES,
  SYSTEM_STATUS,
} from '../../core/mock';
import { AuthService } from '../../core/services';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [PageHeaderComponent, CardComponent, IconComponent, BadgeComponent, AvatarComponent, ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {
  private readonly authService = inject(AuthService);

  readonly stats = DASHBOARD_STATS;
  readonly activity = RECENT_ACTIVITY;
  readonly quickActions = QUICK_ACTIONS;
  readonly recentUsers = RECENT_USERS;
  readonly systemStatus = SYSTEM_STATUS;
  readonly revenueSeries = REVENUE_SERIES;

  readonly maxRevenue = Math.max(...REVENUE_SERIES.map((r) => r.value));

  readonly welcomeName = computed(() => this.authService.currentUser()?.fullName?.split(' ')[0] ?? 'there');

  statusVariant(status: 'Active' | 'Inactive' | 'Pending'): 'success' | 'neutral' | 'warning' {
    if (status === 'Active') return 'success';
    if (status === 'Pending') return 'warning';
    return 'neutral';
  }

  barHeight(value: number): number {
    return Math.max(8, Math.round((value / this.maxRevenue) * 100));
  }
}
