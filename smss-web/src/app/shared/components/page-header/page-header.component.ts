import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { BreadcrumbComponent } from '../breadcrumb/breadcrumb.component';
import { IconComponent } from '../icon/icon.component';
import { BreadcrumbItem } from '../../../core/models';

@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [BreadcrumbComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-header.component.html',
  styleUrl: './page-header.component.scss',
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly subtitle = input<string | undefined>(undefined);
  readonly icon = input<string | undefined>(undefined);
  readonly breadcrumbs = input<BreadcrumbItem[] | undefined>(undefined);
}
