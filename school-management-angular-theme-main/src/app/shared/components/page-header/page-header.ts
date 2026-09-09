import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../icon/icon';

export interface Crumb { label: string; link?: string; }

@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-header animate-in">
      @if (crumbs().length) {
        <nav class="crumbs">
          @for (c of crumbs(); track c.label; let last = $last) {
            @if (c.link && !last) {
              <a [routerLink]="c.link">{{ c.label }}</a>
            } @else {
              <span [class.current]="last">{{ c.label }}</span>
            }
            @if (!last) { <app-icon name="chevron-right" [size]="13" class="sep" /> }
          }
        </nav>
      }
      <div class="header-row">
        <div class="titles">
          <h1 class="page-title">{{ title() }}</h1>
          @if (subtitle()) { <p class="subtitle text-secondary">{{ subtitle() }}</p> }
        </div>
        <div class="actions">
          <ng-content></ng-content>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-header { margin-bottom: var(--space-6); }
    .crumbs { display: flex; align-items: center; gap: 6px; font-size: 12.5px; color: var(--text-muted); margin-bottom: var(--space-2); flex-wrap: wrap; }
    .crumbs a { color: var(--text-secondary); }
    .crumbs a:hover { color: var(--primary); }
    .crumbs .current { color: var(--text-primary); font-weight: 600; }
    .sep { color: var(--text-muted); opacity: 0.7; }
    .header-row { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-4); flex-wrap: wrap; }
    .subtitle { margin-top: 4px; font-size: 13px; }
    .actions { display: flex; gap: var(--space-2); flex-wrap: wrap; }
  `],
})
export class PageHeaderComponent {
  title = input.required<string>();
  subtitle = input<string>('');
  crumbs = input<Crumb[]>([]);
}
