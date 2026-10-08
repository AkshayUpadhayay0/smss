import { Injectable, signal } from '@angular/core';

export type ToastType = 'success' | 'error' | 'info' | 'warning';

export interface Toast {
  id: number;
  type: ToastType;
  message: string;
  title?: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  private readonly _toasts = signal<Toast[]>([]);
  readonly toasts = this._toasts.asReadonly();

  success(message: string, title?: string, durationMs = 4000): void {
    this.show('success', message, title, durationMs);
  }
  error(message: string, title?: string, durationMs = 6000): void {
    this.show('error', message, title, durationMs);
  }
  /** Alias of error() for destructive/failed actions. */
  danger(message: string, title?: string, durationMs = 6000): void {
    this.show('error', message, title, durationMs);
  }
  info(message: string, title?: string, durationMs = 4000): void {
    this.show('info', message, title, durationMs);
  }
  warning(message: string, title?: string, durationMs = 5000): void {
    this.show('warning', message, title, durationMs);
  }

  dismiss(id: number): void {
    this._toasts.update((list) => list.filter((t) => t.id !== id));
  }

  private show(type: ToastType, message: string, title: string | undefined, durationMs: number): void {
    const id = this.nextId++;
    this._toasts.update((list) => [...list, { id, type, message, title }]);
    if (durationMs > 0) {
      setTimeout(() => this.dismiss(id), durationMs);
    }
  }
}
