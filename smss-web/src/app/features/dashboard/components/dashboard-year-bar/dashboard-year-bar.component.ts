import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { IconComponent, SelectComponent, SelectOption } from '../../../../shared/components';

/**
 * "Which academic year am I looking at?" for the Principal and Accountant dashboards: a selector over every year of the school, and a sentence saying so when the year shown is
 * not the school's current one. Choosing a year only changes what is shown; it never changes the school's current year.
 */
@Component({
  selector: 'app-dashboard-year-bar',
  imports: [ReactiveFormsModule, SelectComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="year-bar">
      <app-select label="Academic year" [options]="options()" [formControl]="control" />
      @if (note()) {
        <p class="note" role="status" data-note="viewing"><app-icon name="info" [size]="16" /><span>{{ note() }}</span></p>
      }
    </div>
  `,
  styles: `
    .year-bar {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: var(--space-4, 1rem);
      margin: 0 0 var(--space-4, 1rem);
    }
    app-select {
      min-width: 14rem;
      max-width: 100%;
    }
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
  private readonly destroyRef = inject(DestroyRef);

  readonly options = input<SelectOption[]>([]);
  /** The id of the year on screen, as a string ('' until known). */
  readonly selected = input('');
  /** The server's sentence about the viewed year; null when it is the current year. */
  readonly note = input<string | null>(null);
  /** The year the user chose (undefined = the current year). */
  readonly yearChange = output<number | undefined>();

  protected readonly control = new FormControl('', { nonNullable: true });

  constructor() {
    // show the year actually on screen without announcing it as a user choice
    effect(() => this.control.setValue(this.selected(), { emitEvent: false }));
    this.control.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.yearChange.emit(v ? Number(v) : undefined));
  }
}
