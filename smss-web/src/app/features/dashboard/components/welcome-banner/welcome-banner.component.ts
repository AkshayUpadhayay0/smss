import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from '../../../../shared/components';
import type { IconName } from '../../../../shared/components/icon/icons';

/** The campus-illustration banner at the top of every role's dashboard. Pure presentation: the page decides the words. */
@Component({
  selector: 'app-welcome-banner',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './welcome-banner.component.scss',
  template: `
    <section class="welcome" aria-label="Welcome">
      <div class="text">
        <h1>{{ title() }} <span aria-hidden="true">👋</span></h1>
        <p>{{ subtitle() }}</p>
        @if (badge()) {
          <span class="badge"><app-icon [name]="badgeIcon()" [size]="16" />{{ badge() }}</span>
        }
      </div>
      <div class="extra"><ng-content /></div>
    </section>
  `,
})
export class WelcomeBannerComponent {
  readonly title = input.required<string>();
  readonly subtitle = input('');
  readonly badge = input<string | null>(null);
  readonly badgeIcon = input<IconName>('calendar');
}
