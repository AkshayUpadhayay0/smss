import { DISCLOSURE_VERSION } from './copy';
import type { StopReason } from './ping';

/** The tiny key-value surface used here: expo-secure-store in the app, a Map in tests. */
export interface KeyValue {
  get(key: string): Promise<string | null>;
  set(key: string, value: string): Promise<void>;
  del(key: string): Promise<void>;
}

const KEY_TOKEN = 'smss.duty.token';
const KEY_STOPPED = 'smss.duty.stopped';
const KEY_DISCLOSURE = 'smss.duty.disclosure';

export interface StoppedRecord {
  reason: StopReason;
  at: string;
}

const REASONS: readonly string[] = ['UNKNOWN_TOKEN', 'SESSION_ENDED', 'SESSION_EXPIRED', 'DRIVER_DEACTIVATED', 'NO_TOKEN'];

/**
 * Duty state kept on the phone: the tracking token (a secret: only ever in secure storage, never logged), the reason sharing last stopped on its own,
 * and whether the driver accepted the disclosure. The background task and the screens read the same storage, so they never disagree.
 */
export function createDutyStore(kv: KeyValue) {
  return {
    readToken: (): Promise<string | null> => kv.get(KEY_TOKEN),
    writeToken: (token: string): Promise<void> => kv.set(KEY_TOKEN, token),
    clearToken: (): Promise<void> => kv.del(KEY_TOKEN),

    async readStopped(): Promise<StoppedRecord | null> {
      try {
        const raw = await kv.get(KEY_STOPPED);
        const v = raw ? (JSON.parse(raw) as Partial<StoppedRecord>) : null;
        return v && typeof v.reason === 'string' && REASONS.includes(v.reason) && typeof v.at === 'string' ? (v as StoppedRecord) : null;
      } catch {
        return null;
      }
    },
    writeStopped: (reason: StopReason, now = new Date()): Promise<void> => kv.set(KEY_STOPPED, JSON.stringify({ reason, at: now.toISOString() } satisfies StoppedRecord)),
    clearStopped: (): Promise<void> => kv.del(KEY_STOPPED),

    async disclosureAccepted(): Promise<boolean> {
      return (await kv.get(KEY_DISCLOSURE)) === String(DISCLOSURE_VERSION);
    },
    acceptDisclosure: (): Promise<void> => kv.set(KEY_DISCLOSURE, String(DISCLOSURE_VERSION)),
  };
}

export type DutyStore = ReturnType<typeof createDutyStore>;
