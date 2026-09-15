import { Injectable, signal } from '@angular/core';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: 'danger' | 'primary';
}

interface ConfirmState extends ConfirmOptions {
  resolve: (value: boolean) => void;
}

/**
 * Single confirmation dialog shared by the whole app. Components call
 * `confirmDialogService.confirm({...})` and `await` the promise instead of
 * each page implementing its own "Are you sure?" modal.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  readonly state = signal<ConfirmState | null>(null);

  confirm(options: ConfirmOptions): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      this.state.set({
        confirmLabel: 'Confirm',
        cancelLabel: 'Cancel',
        variant: 'primary',
        ...options,
        resolve,
      });
    });
  }

  resolve(result: boolean): void {
    const current = this.state();
    if (!current) return;
    current.resolve(result);
    this.state.set(null);
  }
}
