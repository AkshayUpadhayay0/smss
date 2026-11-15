import { buildPingBody, classifyPing, newestLocation, type DeviceLocation, type PingBody, type PingHttpResult, type PingOutcome, type StopReason } from './ping';

/** Everything the ping needs from the outside, injected so the logic is testable without React Native. */
export interface PingDeps {
  /** Reads the tracking token from secure storage. Called on EVERY ping: never cached, because a resume at duty start issues a new token and kills the old one. */
  readToken(): Promise<string | null>;
  /** Sends one fix with the X-Duty-Token header. Returns {kind:'network'} when no response arrived. Must never use the login (JWT or refresh token). */
  send(token: string, body: PingBody): Promise<PingHttpResult>;
  /** Stop the location service, delete the token, remember why. */
  onStop(reason: StopReason): Promise<void>;
}

export type RunOutcome = PingOutcome | { action: 'NONE' };

/**
 * One background ping. Never throws and never logs (no coordinates, tokens or bodies).
 * No token on disk means duty is not authorised on this phone any more: stop the service instead of sending nothing forever.
 */
export async function runPing(deps: PingDeps, locations: readonly DeviceLocation[] | null | undefined): Promise<RunOutcome> {
  const loc = newestLocation(locations);
  if (!loc) return { action: 'NONE' };
  try {
    const token = await deps.readToken();
    if (!token) {
      await deps.onStop('NO_TOKEN');
      return { action: 'STOP', stopReason: 'NO_TOKEN' };
    }
    const outcome = classifyPing(await deps.send(token, buildPingBody(loc)));
    if (outcome.action === 'STOP' && outcome.stopReason) await deps.onStop(outcome.stopReason);
    return outcome;
  } catch {
    return { action: 'RETRY' };
  }
}
