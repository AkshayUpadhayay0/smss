import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Breadcrumb, CardComponent, IconComponent, IconName, PageHeaderComponent } from '../../shared/components';

/**
 * Stand-in for screens that aren't built yet. Configured purely through route `data`
 * (bound to inputs via withComponentInputBinding).
 */
@Component({
  selector: 'app-placeholder-page',
  imports: [PageHeaderComponent, CardComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './placeholder-page.component.scss',
  template: `
    <app-page-header [title]="title()" [subtitle]="subtitle()" [icon]="icon()" [breadcrumbs]="breadcrumbs()" />
    <app-card>
      <div class="empty">
        <app-icon name="inbox" [size]="36" />
        <p>This screen is coming soon.</p>
      </div>
    </app-card>
  `,
})
export class PlaceholderPageComponent {
  readonly title = input('Page');
  readonly subtitle = input<string>();
  readonly icon = input<IconName>('layout-dashboard');
  readonly breadcrumbs = input<Breadcrumb[]>([]);
}
