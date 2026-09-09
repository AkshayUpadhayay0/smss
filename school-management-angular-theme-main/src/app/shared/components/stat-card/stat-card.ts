import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from '../icon/icon';

@Component({
  selector: 'app-stat-card',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stat-card card card-pad animate-in">
      <div class="row">
        <div class="icon-wrap" [style.background]="bg()" [style.color]="color()">
          <app-icon [name]="icon()" [size]="22" [strokeWidth]="2" />
        </div>
        @if (change()) {
          <span class="change" [class.up]="trend() === 'up'" [class.down]="trend() === 'down'">
            <app-icon [name]="trend() === 'down' ? 'arrow-down' : 'arrow-up'" [size]="12" [strokeWidth]="2.4" />
            {{ change() }}
          </span>
        }
      </div>
      <div class="value">{{ value() }}</div>
      <div class="label text-secondary">{{ label() }}</div>
      @if (footnote()) { <div class="footnote text-muted">{{ footnote() }}</div> }
    </div>
  `,
  styles: [`
    .stat-card { min-width: 0; transition: transform var(--transition-base), box-shadow var(--transition-base); }
    .stat-card:hover { transform: translateY(-2px); box-shadow: var(--shadow-md); }
    .row { display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--space-4); }
    .icon-wrap { width: 44px; height: 44px; border-radius: var(--radius-md); display: flex; align-items: center; justify-content: center; }
    .change { display: inline-flex; align-items: center; gap: 3px; font-size: 12px; font-weight: 700; padding: 3px 8px; border-radius: var(--radius-full); }
    .change.up { color: var(--success); background: var(--success-light); }
    .change.down { color: var(--danger); background: var(--danger-light); }
    .value { font-size: 26px; font-weight: 800; font-family: var(--font-display); line-height: 1.2; }
    .label { font-size: 13px; margin-top: 2px; }
    .footnote { font-size: 11.5px; margin-top: 6px; }
  `],
})
export class StatCardComponent {
  label = input.required<string>();
  value = input.required<string>();
  icon = input<string>('activity');
  color = input<string>('var(--primary)');
  bg = input<string>('var(--primary-light)');
  change = input<string>('');
  trend = input<'up' | 'down' | ''>('');
  footnote = input<string>('');
}
