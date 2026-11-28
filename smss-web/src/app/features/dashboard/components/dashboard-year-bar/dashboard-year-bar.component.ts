import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from '../../../../shared/components';

/**
 * Says which academic year a dashboard is showing when it is not the school's current one (the server's sentence). The year itself is chosen ONCE, in the header (YearContextService):
 * dashboards no longer carry a picker of their own.
 */
@Component({
  selector: 'app-dashboard-year-bar',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (note()) {
      <div class="year-bar">
        <p class="note" role="status" data-note="viewing"><app-icon name="info" [size]="16" /><span>{{ note() }}</span></p>
      </div>
    }
  `,
  styles: `
    .year-bar { margin: 0 0 var(--space-4, 1rem); }
    .note {
      display: flex;
      align-items: center;
      gap: var(--space-2, 0.5rem);
      margin: 0;
      padding: 0.5rem 0.75rem;
      border-radius: var(--radius-md, 8px);
      background: var(--color-info-50, #eff6ff);
      color: var(--color-info-700, #1d4ed8);
      font-size: 0.875rem;
    }
  `,
})
export class DashboardYearBarComponent {
  /** The server's sentence about the viewed year; null when it is the current year. */
  readonly note = input<string | null>(null);
}
