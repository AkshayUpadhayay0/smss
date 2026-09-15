import { Injectable, signal } from '@angular/core';
import { ToastMessage, ToastVariant } from '../models';

let toastCounter = 0;

/**
 * Global toast queue. Call `toastService.success(...)` etc from any
 * component/service; the single <app-toast-host> mounted in AppComponent
 * renders the queue, so no page needs its own toast markup.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<ToastMessage[]>([]);

  show(variant: ToastVariant, title: string, message?: string, duration = 4000): void {
    const toast: ToastMessage = { id: ++toastCounter, variant, title, message, duration };
    this.toasts.update((list) => [...list, toast]);

    if (duration > 0) {
      setTimeout(() => this.dismiss(toast.id), duration);
    }
  }

  success(title: string, message?: string): void {
    this.show('success', title, message);
  }

  info(title: string, message?: string): void {
    this.show('info', title, message);
  }

  warning(title: string, message?: string): void {
    this.show('warning', title, message);
  }

  danger(title: string, message?: string): void {
    this.show('danger', title, message);
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
