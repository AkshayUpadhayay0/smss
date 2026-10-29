import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ChartComponent } from 'ng-apexcharts';
import type { ApexOptions } from 'ng-apexcharts';

/**
 * One place that talks to ng-apexcharts: give it ApexOptions (built by utils/super-admin-charts) and it draws them. Everything else in the
 * dashboard stays plain Angular, and tests replace just this component.
 */
@Component({
  selector: 'app-chart',
  imports: [ChartComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: ':host { display: block; min-width: 0; }',
  template: `
    @let o = options();
    <div role="img" [attr.aria-label]="label()">
      <apx-chart
        [series]="$any(o.series)"
        [chart]="$any(o.chart)"
        [colors]="o.colors"
        [labels]="o.labels"
        [xaxis]="o.xaxis"
        [yaxis]="o.yaxis"
        [stroke]="o.stroke"
        [fill]="o.fill"
        [dataLabels]="o.dataLabels"
        [grid]="o.grid"
        [legend]="o.legend"
        [tooltip]="o.tooltip"
        [plotOptions]="o.plotOptions"
      />
    </div>
  `,
})
export class ChartHostComponent {
  readonly options = input.required<ApexOptions>();
  /** Spoken description of the chart (the SVG itself is not readable). */
  readonly label = input('Chart');
}
