/**
 * Map settings for the live transport map: the ONLY place the tile provider is named. To change provider edit this file (a paid or self-hosted
 * raster provider, e.g. `https://api.example.com/tiles/{z}/{x}/{y}.png?key=...`), keep the attribution text the new provider requires.
 *
 * OpenStreetMap's public tile server (tile.openstreetmap.org) is free but best-effort (https://operations.osmfoundation.org/policies/tiles/):
 *  - the attribution must stay visible on the map (Leaflet's attribution control shows it);
 *  - web pages must send a valid Referer (the browser does; do not set a Referrer-Policy that strips it);
 *  - honour the tile cache headers (the browser does); no bulk download, no prefetching, no offline use;
 *  - access may be withdrawn at any time, so use a hosted or self-hosted provider before a real production launch.
 * If a Content-Security-Policy is ever added at hosting time, allow `img-src` for the tile host and `connect-src` for the API.
 */
export const MAP_CONFIG = {
  tileUrl: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors',
  maxZoom: 19,
  /** Shown until the first driver reports a position (roughly the middle of India). */
  defaultCenter: [22.5, 79.0] as [number, number],
  defaultZoom: 5,
  /** Zoom used when a driver is picked from the list. */
  focusZoom: 16,
  /** The live board is fetched this often while the tab is visible. */
  pollSeconds: 20,
} as const;
