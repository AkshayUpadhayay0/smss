/**
 * The student's "Where is my bus" map. The ONLY place the tile provider is named. To change provider edit this file and keep the attribution it requires.
 *
 * OpenStreetMap's public tile server (tile.openstreetmap.org) is free but best-effort (https://operations.osmfoundation.org/policies/tiles/):
 *  - the attribution must stay visible on the map (Leaflet's attribution control shows it);
 *  - the map runs inside a WebView, which is a web client: it must send a valid Referer. That is what `baseUrl` is for: the WebView loads the page "as if" from
 *    this address, so every tile request carries it as the Referer. Set it to the address of the SMSS website / school portal before any store release;
 *  - honour the tile cache headers (the WebView does); no bulk download, no prefetching, no offline use;
 *  - access may be withdrawn at any time, so use a hosted or self-hosted provider before a real production launch.
 */
export const MAP_CONFIG = {
  tileUrl: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  maxZoom: 19,
  /** Shown until a bus position arrives (roughly the middle of India). */
  defaultCenter: [22.5, 79.0] as [number, number],
  defaultZoom: 5,
  /** Zoom used when there is exactly one bus to show. */
  focusZoom: 16,
  /** The bus is asked for this often while the screen is open. */
  pollSeconds: 20,
  /** The origin the map page claims (the Referer of every tile request). PLACEHOLDER until the real site address is known. */
  baseUrl: 'https://smss.example.com',
} as const;
