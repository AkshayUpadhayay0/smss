import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface LineSeries { name: string; color: string; values: number[]; }

@Component({
  selector: 'app-line-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="line-chart">
      @if (legend() && series().length > 1) {
        <div class="legend">
          @for (s of series(); track s.name) {
            <span class="legend-item"><span class="dash" [style.background]="s.color"></span>{{ s.name }}</span>
          }
        </div>
      }
      <svg [attr.viewBox]="'0 0 ' + w + ' ' + h" [style.height.px]="height()" preserveAspectRatio="none" class="chart">
        @for (line of gridLines(); track line) {
          <line x1="0" [attr.x2]="w" [attr.y1]="line" [attr.y2]="line" stroke="var(--border)" stroke-width="1" />
        }
        @for (s of computedSeries(); track s.name) {
          <polygon [attr.points]="s.areaPoints" [attr.fill]="s.color" opacity="0.08" />
          <polyline [attr.points]="s.linePoints" fill="none" [attr.stroke]="s.color" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" />
          @for (pt of s.points; track pt.x) {
            <circle [attr.cx]="pt.x" [attr.cy]="pt.y" r="3" [attr.fill]="s.color" />
          }
        }
      </svg>
      <div class="labels">
        @for (label of labels(); track label) { <span>{{ label }}</span> }
      </div>
    </div>
  `,
  styles: [`
    .line-chart { width: 100%; }
    .legend { display: flex; gap: var(--space-4); margin-bottom: var(--space-3); flex-wrap: wrap; }
    .legend-item { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; color: var(--text-secondary); }
    .dash { width: 14px; height: 3px; border-radius: 2px; }
    .chart { width: 100%; display: block; }
    .labels { display: flex; margin-top: 8px; }
    .labels span { flex: 1; text-align: center; font-size: 10.5px; color: var(--text-muted); }
  `],
})
export class LineChartComponent {
  series = input.required<LineSeries[]>();
  labels = input.required<string[]>();
  height = input<number>(220);
  legend = input<boolean>(true);
  w = 600;
  h = 220;

  maxValue = computed(() => {
    const all = this.series().flatMap(s => s.values);
    return Math.max(...all, 1) * 1.15;
  });
  minValue = computed(() => {
    const all = this.series().flatMap(s => s.values);
    return Math.min(...all, 0) * 0.9;
  });

  gridLines = computed(() => [0, this.h * 0.25, this.h * 0.5, this.h * 0.75, this.h]);

  computedSeries = computed(() => {
    const n = this.labels().length;
    const max = this.maxValue();
    const min = this.minValue();
    const range = Math.max(max - min, 1);
    const stepX = n > 1 ? this.w / (n - 1) : this.w;
    return this.series().map(s => {
      const points = s.values.map((v, i) => ({
        x: Math.round(i * stepX),
        y: Math.round(this.h - ((v - min) / range) * this.h),
      }));
      const linePoints = points.map(p => `${p.x},${p.y}`).join(' ');
      const areaPoints = `0,${this.h} ${linePoints} ${this.w},${this.h}`;
      return { name: s.name, color: s.color, points, linePoints, areaPoints };
    });
  });
}
