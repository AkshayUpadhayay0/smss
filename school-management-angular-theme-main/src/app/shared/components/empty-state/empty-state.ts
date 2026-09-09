import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from '../icon/icon';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty-state">
      <div class="icon-circle"><app-icon [name]="icon()" [size]="28" [strokeWidth]="1.6" /></div>
      <div class="title">{{ title() }}</div>
      @if (description()) { <p class="desc text-secondary">{{ description() }}</p> }
      <ng-content></ng-content>
    </div>
  `,
  styles: [`
    .icon-circle {
      width: 60px; height: 60px; border-radius: var(--radius-full); background: var(--surface-alt);
      display: flex; align-items: center; justify-content: center; margin: 0 auto var(--space-4); color: var(--text-muted);
    }
    .title { font-weight: 700; font-size: 15px; margin-bottom: 4px; }
    .desc { font-size: 13px; max-width: 360px; margin: 0 auto var(--space-4); }
  `],
})
export class EmptyStateComponent {
  icon = input<string>('inbox');
  title = input<string>('Nothing here yet');
  description = input<string>('');
}
