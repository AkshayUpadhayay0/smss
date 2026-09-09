import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { NotificationService } from '../../core/services/data.service';
import { AppNotification, NotificationType } from '../../core/models/school.models';

const TYPE_ICON: Record<NotificationType, string> = {
  General: 'bell', Attendance: 'check-square', Fees: 'wallet', Homework: 'clipboard-list',
  Examination: 'award', Emergency: 'alert-triangle',
};

@Component({
  selector: 'app-notification-center',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, TabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notification-center.html',
})
export class NotificationCenterComponent {
  private notificationService = inject(NotificationService);
  notifications = signal<AppNotification[]>([]);
  activeType = signal('all');

  tabs: TabItem[] = [
    { id: 'all', label: 'All', icon: 'inbox' },
    { id: 'General', label: 'General', icon: 'bell' },
    { id: 'Attendance', label: 'Attendance', icon: 'check-square' },
    { id: 'Fees', label: 'Fees', icon: 'wallet' },
    { id: 'Homework', label: 'Homework', icon: 'clipboard-list' },
    { id: 'Examination', label: 'Examination', icon: 'award' },
    { id: 'Emergency', label: 'Emergency', icon: 'alert-triangle' },
  ];

  constructor() {
    this.notificationService.getAll().subscribe(list => this.notifications.set(list));
  }

  filtered = computed(() => {
    if (this.activeType() === 'all') return this.notifications();
    return this.notifications().filter(n => n.type === this.activeType());
  });

  iconFor(type: NotificationType): string {
    return TYPE_ICON[type] ?? 'bell';
  }

  markRead(id: string): void {
    this.notifications.update(list => list.map(n => n.id === id ? { ...n, read: true } : n));
  }

  markAllRead(): void {
    this.notifications.update(list => list.map(n => ({ ...n, read: true })));
  }
}
