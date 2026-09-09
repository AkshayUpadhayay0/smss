import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-progress-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="progress-wrap">
      @if (label()) {
        <div class="progress-label">
          <span>{{ label() }}</span>
          @if (showValue()) { <span class="font-semibold">{{ value() }}%</span> }
        </div>
      }
      <div class="track" [style.height.px]="thickness()">
        <div class="fill" [style.width.%]="value()" [style.background]="color()"></div>
      </div>
    </div>
  `,
  styles: [`
    .progress-wrap { width: 100%; }
    .progress-label { display: flex; justify-content: space-between; font-size: 12.5px; margin-bottom: 6px; color: var(--text-secondary); }
    .track { width: 100%; background: var(--surface-alt); border-radius: var(--radius-full); overflow: hidden; }
    .fill { height: 100%; border-radius: var(--radius-full); transition: width 0.6s ease; }
  `],
})
export class ProgressBarComponent {
  value = input<number>(0);
  label = input<string>('');
  color = input<string>('var(--primary)');
  thickness = input<number>(8);
  showValue = input<boolean>(true);
}
