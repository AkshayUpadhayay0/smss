import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { ProgressBarComponent } from '../../shared/components/progress-bar/progress-bar';
import { ModalComponent } from '../../shared/components/modal/modal';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { PaginatorComponent } from '../../shared/components/paginator/paginator';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state';

@Component({
  selector: 'app-ui-showcase',
  standalone: true,
  imports: [
    CommonModule, PageHeaderComponent, IconComponent, AvatarComponent, StatusBadgeComponent,
    ProgressBarComponent, ModalComponent, TabsComponent, PaginatorComponent, EmptyStateComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-showcase.html',
})
export class UiShowcaseComponent {
  modalOpen = signal(false);
  activeTab = signal('tab1');
  activePillTab = signal('p1');
  page = signal(3);
  accordionOpen = signal<Set<number>>(new Set([0]));

  tabItems: TabItem[] = [
    { id: 'tab1', label: 'Overview', icon: 'grid' },
    { id: 'tab2', label: 'Details', icon: 'file-text', count: 4 },
    { id: 'tab3', label: 'Settings', icon: 'settings' },
  ];
  pillItems: TabItem[] = [
    { id: 'p1', label: 'Daily' },
    { id: 'p2', label: 'Weekly' },
    { id: 'p3', label: 'Monthly' },
  ];

  faqs = [
    { q: 'How do I reset a student\u2019s password?', a: 'Go to Students → select the student → Account Settings → Reset Password. A temporary password will be generated.' },
    { q: 'Can I customize the report card template?', a: 'Yes, report card templates can be customized from Settings → Academic Settings → Grading & Report Cards.' },
    { q: 'How is attendance percentage calculated?', a: 'Attendance percentage is calculated as (Days Present ÷ Total Working Days) × 100 for the selected academic session.' },
  ];

  toggleAccordion(i: number): void {
    const next = new Set(this.accordionOpen());
    if (next.has(i)) next.delete(i); else next.add(i);
    this.accordionOpen.set(next);
  }
}
