import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../shared/components/card/card.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { ButtonComponent } from '../../shared/components/button/button.component';
import { BadgeComponent } from '../../shared/components/badge/badge.component';
import { AlertComponent } from '../../shared/components/alert/alert.component';
import { AvatarComponent } from '../../shared/components/avatar/avatar.component';
import { SpinnerComponent } from '../../shared/components/spinner/spinner.component';
import { SkeletonComponent } from '../../shared/components/skeleton/skeleton.component';
import { ProgressBarComponent } from '../../shared/components/progress-bar/progress-bar.component';
import { CircularProgressComponent } from '../../shared/components/circular-progress/circular-progress.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { BreadcrumbComponent } from '../../shared/components/breadcrumb/breadcrumb.component';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs.component';
import { AccordionComponent, AccordionItem } from '../../shared/components/accordion/accordion.component';
import { DropdownComponent } from '../../shared/components/dropdown/dropdown.component';
import { TooltipDirective } from '../../shared/directives/tooltip.directive';
import { ToastService } from '../../core/services';

const TABS: TabItem[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'activity', label: 'Activity' },
  { id: 'settings', label: 'Settings' },
];

const ACCORDION_ITEMS: AccordionItem[] = [
  { id: 'a1', title: 'What is smss-web?', content: 'A reusable Angular SaaS admin template with a multi-organization theme system.' },
  { id: 'a2', title: 'Can I add a new theme?', content: 'Yes — add a new SCSS partial under styles/themes and register it in the theme catalog.' },
  { id: 'a3', title: 'Is a backend required?', content: 'No. The template runs entirely on mock data and localStorage until you connect a real API.' },
];

@Component({
  selector: 'app-ui-elements-page',
  standalone: true,
  imports: [
    PageHeaderComponent,
    CardComponent,
    IconComponent,
    ButtonComponent,
    BadgeComponent,
    AlertComponent,
    AvatarComponent,
    SpinnerComponent,
    SkeletonComponent,
    ProgressBarComponent,
    CircularProgressComponent,
    EmptyStateComponent,
    BreadcrumbComponent,
    TabsComponent,
    AccordionComponent,
    DropdownComponent,
    TooltipDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-elements.component.html',
  styleUrl: './ui-elements.component.scss',
})
export class UiElementsPageComponent {
  private readonly toastService = inject(ToastService);

  readonly tabs = TABS;
  readonly accordionItems = ACCORDION_ITEMS;
  readonly loadingButton = signal(false);
  readonly sampleBreadcrumb = [
    { label: 'Home', url: '/dashboard' },
    { label: 'Management', url: '/table' },
    { label: 'Users', url: '/table' },
    { label: 'Details' },
  ];

  simulateButtonLoading(): void {
    this.loadingButton.set(true);
    setTimeout(() => this.loadingButton.set(false), 1500);
  }

  showToast(variant: 'success' | 'info' | 'warning' | 'danger'): void {
    const copy: Record<string, [string, string]> = {
      success: ['Saved successfully', 'Your changes have been saved.'],
      info: ['Heads up', 'This is an informational toast message.'],
      warning: ['Careful', 'This action might have side effects.'],
      danger: ['Something went wrong', 'Please try that action again.'],
    };
    const [title, message] = copy[variant];
    this.toastService.show(variant, title, message);
  }
}
