import { Injectable, signal } from '@angular/core';

export interface ConfirmOptions {
  title?: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  /** 'danger' renders the confirm button red. */
  variant?: 'primary' | 'danger';
}

export interface ConfirmState extends Required<Omit<ConfirmOptions, 'message'>> {
  message: string;
}

@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  private resolver: ((value: boolean) => void) | null = null;
  private readonly _state = signal<ConfirmState | null>(null);
  readonly state = this._state.asReadonly();

  confirm(options: ConfirmOptions): Promise<boolean> {
    // Resolve any dialog still open as cancelled.
    this.resolver?.(false);
    this._state.set({
      title: options.title ?? 'Are you sure?',
      message: options.message,
      confirmText: options.confirmText ?? 'Confirm',
      cancelText: options.cancelText ?? 'Cancel',
      variant: options.variant ?? 'primary',
    });
    return new Promise<boolean>((resolve) => (this.resolver = resolve));
  }

  /** Called by the dialog host component. */
  resolve(result: boolean): void {
    this.resolver?.(result);
    this.resolver = null;
    this._state.set(null);
  }
}
