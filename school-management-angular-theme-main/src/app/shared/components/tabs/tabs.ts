import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { IconComponent } from '../icon/icon';

export interface TabItem { id: string; label: string; icon?: string; count?: number; }

@Component({
  selector: 'app-tabs',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tabs" [class.pill]="variant() === 'pill'">
      @for (t of items(); track t.id) {
        <button
          class="tab"
          [class.active]="t.id === active()"
          (click)="select.emit(t.id)"
          type="button"
        >
          @if (t.icon) { <app-icon [name]="t.icon" [size]="16" /> }
          {{ t.label }}
          @if (t.count !== undefined) { <span class="count">{{ t.count }}</span> }
        </button>
      }
    </div>
  `,
  styles: [`
    .tabs { display: flex; gap: var(--space-2); border-bottom: 1px solid var(--border); overflow-x: auto; }
    .tab {
      display: inline-flex; align-items: center; gap: 6px;
      padding: 11px 4px; margin-right: var(--space-5);
      background: transparent; border: none; border-bottom: 2px solid transparent;
      color: var(--text-secondary); font-size: 13.5px; font-weight: 600; cursor: pointer;
      white-space: nowrap; transition: color var(--transition-fast), border-color var(--transition-fast);
    }
    .tab:hover { color: var(--text-primary); }
    .tab.active { color: var(--primary); border-bottom-color: var(--primary); }
    .count { background: var(--surface-alt); color: var(--text-secondary); border-radius: var(--radius-full); padding: 1px 7px; font-size: 11px; }
    .tab.active .count { background: var(--primary-light); color: var(--primary); }

    .tabs.pill { border-bottom: none; background: var(--surface-alt); padding: 4px; border-radius: var(--radius-full); display: inline-flex; }
    .tabs.pill .tab { border-bottom: none; border-radius: var(--radius-full); padding: 7px 16px; margin-right: 0; }
    .tabs.pill .tab.active { background: var(--primary); color: #fff; }
  `],
})
export class TabsComponent {
  items = input<TabItem[]>([]);
  active = input<string>('');
  variant = input<'underline' | 'pill'>('underline');
  select = output<string>();
}
