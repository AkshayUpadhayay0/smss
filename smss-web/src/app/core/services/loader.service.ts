import { Injectable, signal } from '@angular/core';

/**
 * Global full-page loader flag. Prefer local `loading` booleans for
 * inline/button loaders; use this only for page/route-level transitions.
 */
@Injectable({ providedIn: 'root' })
export class LoaderService {
  readonly isLoading = signal(false);
  private pending = 0;

  show(): void {
    this.pending += 1;
    this.isLoading.set(true);
  }

  hide(): void {
    this.pending = Math.max(0, this.pending - 1);
    if (this.pending === 0) {
      this.isLoading.set(false);
    }
  }
}
