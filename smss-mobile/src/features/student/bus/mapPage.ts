import { LEAFLET_CSS, LEAFLET_JS } from './leafletAssets.generated';

export interface MapPageOptions {
  tileUrl: string;
  /** HTML shown in Leaflet's attribution control. Must stay visible (OpenStreetMap's tile policy). */
  attribution: string;
  center: readonly [number, number];
  zoom: number;
  maxZoom: number;
  focusZoom: number;
}

/**
 * The HTML of the map page, with Leaflet INLINED (no CDN: nothing but the map tiles is fetched). The page exposes window.__setBuses(points); the app calls it with
 * injectJavaScript. Names and labels are put into the DOM with textContent, never as HTML, so a driver's name cannot inject markup. Markers are updated in place
 * and the view is fitted to them ONLY the first time there is something to show (the student's own pan and zoom are never overridden).
 * The tile requests come from this page, so the WebView's baseUrl is their Referer (see src/config/map.ts).
 */
export function buildMapPage(o: MapPageOptions, assets: { js: string; css: string } = { js: LEAFLET_JS, css: LEAFLET_CSS }): string {
  const config = JSON.stringify({ tileUrl: o.tileUrl, attribution: o.attribution, center: o.center, zoom: o.zoom, maxZoom: o.maxZoom, focusZoom: o.focusZoom }).replace(/</g, '\\u003c');
  return `<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1">
<style>${assets.css}</style>
<style>
  html, body, #map { height: 100%; margin: 0; padding: 0; background: #EDE9FE; }
  .leaflet-container { font: 12px/1.4 -apple-system, Roboto, sans-serif; }
  .pin { display: inline-flex; align-items: center; gap: 6px; transform: translate(-12px, -12px); padding: 4px 10px 4px 6px; border: 2px solid #fff; border-radius: 999px;
         box-shadow: 0 2px 8px rgba(30,27,46,.4); white-space: nowrap; font-weight: 600; font-size: 13px; color: #fff; background: #047857; }
  .pin .dot { width: 10px; height: 10px; border-radius: 50%; background: #fff; }
  .pin.stale { background: #92400E; }
  .pin .state { padding-left: 6px; border-left: 1px solid rgba(255,255,255,.55); font-weight: 500; }
</style>
</head>
<body>
<div id="map"></div>
<script>${assets.js.replace(/<\/script/gi, '<\\/script')}</script>
<script>
(function () {
  var cfg = ${config};
  var map = L.map('map', { zoomControl: true, attributionControl: true }).setView(cfg.center, cfg.zoom);
  L.tileLayer(cfg.tileUrl, { maxZoom: cfg.maxZoom, attribution: cfg.attribution }).addTo(map);
  var markers = {};
  var fitted = false;

  function icon(p) {
    var pin = document.createElement('div');
    pin.className = 'pin ' + (p.status === 'STALE' ? 'stale' : 'live');
    var dot = document.createElement('span'); dot.className = 'dot';
    var text = document.createElement('span'); text.textContent = p.label;
    var state = document.createElement('span'); state.className = 'state'; state.textContent = p.status === 'STALE' ? 'Last known' : 'Live';
    pin.appendChild(dot); pin.appendChild(text); pin.appendChild(state);
    var holder = document.createElement('div'); holder.appendChild(pin);
    return L.divIcon({ className: '', html: holder.innerHTML, iconSize: [0, 0] });
  }

  window.__setBuses = function (points) {
    var wanted = {};
    for (var i = 0; i < points.length; i++) wanted[points[i].id] = points[i];
    for (var id in markers) { if (!wanted[id]) { map.removeLayer(markers[id]); delete markers[id]; } }
    for (var key in wanted) {
      var p = wanted[key], ll = [p.lat, p.lng];
      if (markers[key]) { markers[key].setLatLng(ll); markers[key].setIcon(icon(p)); }
      else { markers[key] = L.marker(ll, { icon: icon(p), title: p.label, keyboard: false }).addTo(map); }
    }
    if (!fitted && points.length) {
      fitted = true;
      if (points.length === 1) map.setView([points[0].lat, points[0].lng], cfg.focusZoom);
      else map.fitBounds(L.latLngBounds(points.map(function (q) { return [q.lat, q.lng]; })), { padding: [40, 40], maxZoom: cfg.focusZoom });
    }
  };
})();
</script>
</body>
</html>`;
}
