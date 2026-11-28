import { effect, inject, untracked } from '@angular/core';
import { FormControl } from '@angular/forms';
import { YearContextService } from '../services/year-context.service';

/**
 * Makes a page follow the app's viewing year: calls <paramref name="load"/> with the viewing year's id each time it is known or changes. Call it from a component constructor (it creates an effect).
 * - years still loading: waits (the page keeps its own loading state), so nothing is requested for a year nobody chose yet
 * - ready: load(id), with that explicit id (the server validates it against the token's school)
 * - no selector for this user, no years yet, or the years could not be loaded: load(undefined), i.e. the server's own current year, exactly as before
 */
export function followViewingYear(load: (academicYearId: number | undefined) => void): void {
  const year = inject(YearContextService);
  effect(() => {
    const state = year.state();
    const id = year.viewingYearId();
    untracked(() => {
      if (state === 'loading') return;
      load(state === 'ready' && id !== null ? id : undefined);
    });
  });
}

/**
 * For pages that keep a (now hidden) year form control: keeps it equal to the app's viewing year, so their existing load logic is unchanged. Call from a constructor.
 * Returns whether the viewing year can be relied on: false when the years could not be loaded or the user has no selector, so the page falls back to its own default (the current year).
 */
export function syncYearControl(control: FormControl<string>): { usable: () => boolean; waiting: () => boolean } {
  const year = inject(YearContextService);
  effect(() => {
    const id = year.viewingYearId();
    if (id !== null) untracked(() => control.setValue(String(id)));
  });
  return { usable: () => year.state() === 'ready', waiting: () => year.state() === 'loading' };
}
