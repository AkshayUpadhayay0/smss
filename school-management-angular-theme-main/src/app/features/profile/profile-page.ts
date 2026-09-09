import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { AuthService } from '../../core/services/auth.service';
import * as M from '../../core/services/mock-data';

@Component({
  selector: 'app-profile-page',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, AvatarComponent, TabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './profile-page.html',
})
export class ProfilePageComponent {
  auth = inject(AuthService);
  activeTab = signal('info');

  tabs: TabItem[] = [
    { id: 'info', label: 'Personal Information', icon: 'user' },
    { id: 'activity', label: 'Activity', icon: 'activity' },
    { id: 'security', label: 'Password', icon: 'lock' },
  ];

  activities = M.ACTIVITIES;
}
