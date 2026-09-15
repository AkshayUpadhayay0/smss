import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../icon/icon.component';
import { BreadcrumbItem } from '../../../core/models';

@Component({
  selector: 'app-breadcrumb',
  standalone: true,
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './breadcrumb.component.html',
  styleUrl: './breadcrumb.component.scss',
})
export class BreadcrumbComponent {
  /** Provide explicitly, or omit to use BreadcrumbService via the page-header. */
  readonly items = input.required<BreadcrumbItem[]>();
}
