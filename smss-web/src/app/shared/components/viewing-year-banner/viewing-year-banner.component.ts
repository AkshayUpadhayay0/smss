import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { YearContextService } from '../../../core/services/year-context.service';
import { IconComponent } from '../icon/icon.component';

/**
 * Put on every DATA-ENTRY page (attendance marking, homework, class tests, timetable activation, admissions, existing-student entry, import). Those pages always act on the school's
 * CURRENT academic year whatever the header's viewing year is, so when the two differ this says so in words: what is entered here is not entered into the year being viewed.
 * Shows nothing when the viewing year is the current one (and for roles that have no selector).
 */
@Component({
  selector: 'app-viewing-year-banner',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (year.isNonCurrent()) {
      <p class="viewing-year-banner" role="status" data-viewing-year-banner>
        <app-icon name="info" [size]="16" />
        <span>
          The header is showing <strong>{{ year.viewingYearName() }}</strong>, but this page always works on the current year<strong> {{ year.currentYear()?.yearName }}</strong>: what you enter here goes into
          {{ year.currentYear()?.yearName }}, not {{ year.viewingYearName() }}.
          <button type="button" class="back" data-viewing-year-back (click)="year.useCurrent()">Show the current year in the header</button>
        </span>
      </p>
    }
  `,
  styles: `
    :host { display: block; }
    .viewing-year-banner {
      display: flex;
      align-items: flex-start;
      gap: var(--space-2, 0.5rem);
      margin: 0 0 var(--space-4, 1rem);
      padding: var(--space-3, 0.75rem) var(--space-4, 1rem);
      border-radius: var(--radius-md, 8px);
      background: #fef0c7;
      border: 1px solid #f7c948;
      color: #93370d;
      font-size: 0.875rem;
      line-height: 1.5;
    }
    .back {
      border: 0;
      background: transparent;
      color: inherit;
      font: inherit;
      text-decoration: underline;
      cursor: pointer;
      padding: 0;
      margin-left: var(--space-2, 0.5rem);
    }
  `,
})
export class ViewingYearBannerComponent {
  protected readonly year = inject(YearContextService);
}
