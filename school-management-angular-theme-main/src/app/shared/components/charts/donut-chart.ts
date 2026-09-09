import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface DonutSlice { label: string; value: number; color: string; }

@Component({
  selector: 'app-donut-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="donut-wrap">
      <svg [attr.viewBox]="'0 0 ' + size + ' ' + size" [style.width.px]="size" [style.height.px]="size">
        <circle [attr.cx]="size/2" [attr.cy]="size/2" [attr.r]="radius" fill="none" stroke="var(--border)" [attr.stroke-width]="thickness()" />
        @for (seg of segments(); track seg.label) {
          <circle
            [attr.cx]="size/2" [attr.cy]="size/2" [attr.r]="radius" fill="none"
            [attr.stroke]="seg.color" [attr.stroke-width]="thickness()"
            [attr.stroke-dasharray]="seg.dash + ' ' + seg.gap"
            [attr.stroke-dashoffset]="seg.offset"
            stroke-linecap="round"
            [attr.transform]="'rotate(-90 ' + size/2 + ' ' + size/2 + ')'"
            style="transition: stroke-dashoffset 0.6s ease"
          />
        }
      </svg>
      @if (centerLabel()) {
        <div class="center">
          <div class="center-value">{{ centerValue() }}</div>
          <div class="center-label text-muted">{{ centerLabel() }}</div>
        </div>
      }
    </div>
    @if (showLegend()) {
      <div class="legend">
        @for (s of data(); track s.label) {
          <div class="legend-item">
            <span class="dot" [style.background]="s.color"></span>
            <span class="text-secondary">{{ s.label }}</span>
            <span class="font-semibold">{{ s.value }}</span>
          </div>
        }
      </div>
    }
  `,
  styles: [`
    .donut-wrap { position: relative; display: inline-flex; align-items: center; justify-content: center; }
    .center { position: absolute; text-align: center; }
    .center-value { font-size: 22px; font-weight: 800; font-family: var(--font-display); }
    .center-label { font-size: 11px; }
    .legend { display: flex; flex-direction: column; gap: 8px; margin-top: var(--space-4); }
    .legend-item { display: flex; align-items: center; gap: 8px; font-size: 12.5px; }
    .legend-item .font-semibold { margin-left: auto; }
    .dot { width: 9px; height: 9px; border-radius: 50%; flex-shrink: 0; }
  `],
})
export class DonutChartComponent {
  data = input.required<DonutSlice[]>();
  size = 160;
  radius = 62;
  thickness = input<number>(16);
  showLegend = input<boolean>(true);
  centerLabel = input<string>('');

  get circumference(): number { return 2 * Math.PI * this.radius; }

  total = computed(() => this.data().reduce((a, s) => a + s.value, 0) || 1);

  centerValue = computed(() => this.total());

  segments = computed(() => {
    let cumulative = 0;
    const circ = this.circumference;
    return this.data().map(s => {
      const fraction = s.value / this.total();
      const dash = fraction * circ;
      const gap = circ - dash;
      const offset = -cumulative;
      cumulative += dash;
      return { label: s.label, color: s.color, dash, gap, offset };
    });
  });
}
