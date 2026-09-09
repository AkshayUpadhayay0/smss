import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface BarSeries { name: string; color: string; values: number[]; }

@Component({
  selector: 'app-bar-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bar-chart">
      @if (legend() && series().length > 1) {
        <div class="legend">
          @for (s of series(); track s.name) {
            <span class="legend-item"><span class="dot" [style.background]="s.color"></span>{{ s.name }}</span>
          }
        </div>
      }
      <div class="chart-area" [style.height.px]="height()">
        <svg [attr.viewBox]="'0 0 ' + width + ' ' + height()" preserveAspectRatio="none" class="grid-svg">
          @for (line of gridLines(); track line) {
            <line x1="0" [attr.x2]="width" [attr.y1]="line" [attr.y2]="line" stroke="var(--border)" stroke-width="1" />
          }
        </svg>
        <div class="bars">
          @for (label of labels(); track label; let i = $index) {
            <div class="bar-group">
              @for (s of series(); track s.name) {
                <div
                  class="bar"
                  [style.height.%]="pct(s.values[i])"
                  [style.background]="s.color"
                  [title]="s.name + ': ' + s.values[i]"
                ></div>
              }
            </div>
          }
        </div>
      </div>
      <div class="labels">
        @for (label of labels(); track label) { <span>{{ label }}</span> }
      </div>
    </div>
  `,
  styles: [`
    .bar-chart { width: 100%; }
    .legend { display: flex; gap: var(--space-4); margin-bottom: var(--space-3); flex-wrap: wrap; }
    .legend-item { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; color: var(--text-secondary); }
    .dot { width: 8px; height: 8px; border-radius: 2px; }
    .chart-area { position: relative; display: flex; align-items: flex-end; }
    .grid-svg { position: absolute; inset: 0; width: 100%; height: 100%; }
    .bars { position: relative; display: flex; align-items: flex-end; width: 100%; height: 100%; gap: 4px; z-index: 1; }
    .bar-group { flex: 1; display: flex; align-items: flex-end; justify-content: center; gap: 3px; height: 100%; min-width: 0; }
    .bar { flex: 1; max-width: 22px; border-radius: 4px 4px 0 0; min-height: 3px; transition: height 0.5s ease; }
    .labels { display: flex; gap: 4px; margin-top: 8px; }
    .labels span { flex: 1; text-align: center; font-size: 10.5px; color: var(--text-muted); min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  `],
})
export class BarChartComponent {
  series = input.required<BarSeries[]>();
  labels = input.required<string[]>();
  height = input<number>(220);
  legend = input<boolean>(true);
  width = 400;

  maxValue = computed(() => {
    const all = this.series().flatMap(s => s.values);
    return Math.max(...all, 1) * 1.15;
  });

  pct(v: number): number {
    return Math.max((v / this.maxValue()) * 100, 1.5);
  }

  gridLines = computed(() => {
    const h = this.height();
    return [0, h * 0.25, h * 0.5, h * 0.75, h];
  });
}
