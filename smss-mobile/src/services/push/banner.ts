import { useSyncExternalStore } from 'react';
import type { PushMessage } from './pushCore';

// A tiny store for the one in-app banner (a message that arrived while the app is open).
export interface BannerState extends PushMessage {
  id: number;
}

let current: BannerState | null = null;
let nextId = 1;
const listeners = new Set<() => void>();
const emit = () => listeners.forEach((l) => l());

export function showBanner(message: PushMessage): void {
  if (!message.title && !message.body) return;
  current = { id: nextId++, ...message };
  emit();
}

export function dismissBanner(): void {
  current = null;
  emit();
}

export function useBanner(): BannerState | null {
  return useSyncExternalStore(
    (cb) => {
      listeners.add(cb);
      return () => void listeners.delete(cb);
    },
    () => current,
    () => null,
  );
}
