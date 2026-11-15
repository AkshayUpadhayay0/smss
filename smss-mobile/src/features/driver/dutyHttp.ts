import { env } from '../../config/env';
import type { PingBody, PingHttpResult } from './ping';

// The two calls authenticated by the TRACKING TOKEN (header X-Duty-Token), made with plain fetch: never through src/api/client.ts, so the background task
// can never touch the login (JWT / refresh token). The token is passed in by the caller and goes only into the header: it is never logged, put in a URL
// or kept in a variable that outlives the call. No request or response body is logged either.

const TIMEOUT_MS = 15_000;

async function post(path: string, token: string, body?: unknown): Promise<PingHttpResult> {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), TIMEOUT_MS);
  try {
    const res = await fetch(`${env.apiBaseUrl}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Duty-Token': token },
      body: JSON.stringify(body ?? {}),
      signal: ctl.signal,
    });
    let parsed: unknown = null;
    try {
      parsed = await res.json();
    } catch {
      // an empty or non-JSON body (a proxy error page): the status alone decides
    }
    return { kind: 'response', status: res.status, body: parsed };
  } catch {
    return { kind: 'network' }; // offline, DNS, TLS, timeout
  } finally {
    clearTimeout(timer);
  }
}

export const postLocation = (token: string, body: PingBody): Promise<PingHttpResult> => post('/api/DriverDuty/location', token, body);
export const postEndWithToken = (token: string): Promise<PingHttpResult> => post('/api/DriverDuty/end', token);
