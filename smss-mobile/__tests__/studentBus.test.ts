import type { MyBus, MyBusItem } from '../src/api/student';
import { MAP_CONFIG } from '../src/config/map';
import { ageText, buildUpdateScript, deriveBusState, mapPoints, toCard } from '../src/features/student/bus/busView';
import { buildMapPage } from '../src/features/student/bus/mapPage';

const bus = (over: Partial<MyBusItem> = {}): MyBusItem => ({
  driverName: 'Ramesh Kumar', vehicleLabel: 'Bus 1', vehiclePlate: 'UP32AB1234', status: 'LIVE', latitude: 26.85, longitude: 80.95, ageSeconds: 12, morningPickupTime: null, afternoonDropTime: null, ...over,
});
const data = (over: Partial<MyBus> = {}): MyBus => ({ assigned: true, area: { areaName: 'Civil Lines' }, buses: [bus()], scheduled: [], serverTime: '2026-11-14T10:00:00Z', message: null, ...over });

describe('ageText', () => {
  it('speaks in seconds, minutes, hours', () => {
    expect(ageText(null)).toBe('');
    expect(ageText(4)).toBe('just now');
    expect(ageText(45)).toBe('45 s ago');
    expect(ageText(125)).toBe('2 min ago');
    expect(ageText(7300)).toBe('2 h ago');
  });
});

describe('deriveBusState: the screen states', () => {
  it('not assigned (a normal answer, with the server message)', () => {
    expect(deriveBusState(data({ assigned: false, area: null, buses: [], message: 'Ask your school office.' }))).toEqual({ kind: 'NOT_ASSIGNED', message: 'Ask your school office.' });
    expect(deriveBusState({ assigned: false, area: null, buses: [], scheduled: [], serverTime: 'x', message: null }).kind).toBe('NOT_ASSIGNED');
  });
  it('assigned but no bus on duty', () => {
    expect(deriveBusState(data({ buses: [] }))).toMatchObject({ kind: 'NO_BUS', areaName: 'Civil Lines' });
  });
  it('buses on duty keep the server order and carry the area', () => {
    const s = deriveBusState(data({ buses: [bus(), bus({ vehiclePlate: 'UP32CD2222', vehicleLabel: 'Van 2', status: 'STALE', ageSeconds: 200 })] }));
    expect(s.kind).toBe('BUSES');
    if (s.kind === 'BUSES') {
      expect(s.areaName).toBe('Civil Lines');
      expect(s.buses.map((b) => b.vehicleLabel)).toEqual(['Bus 1', 'Van 2']);
    }
  });
});

describe('toCard: what each status says', () => {
  it('LIVE: updated X ago, no warning', () => {
    const c = toCard(bus({ ageSeconds: 12 }));
    expect([c.statusText, c.updatedText, c.warning, c.hasPosition]).toEqual(['Live', 'Updated 12 s ago', null, true]);
  });
  it('STALE: the last position with a clear warning and its age', () => {
    const c = toCard(bus({ status: 'STALE', ageSeconds: 240 }));
    expect(c.updatedText).toBe('Updated 4 min ago');
    expect(c.warning).toContain('last known position');
    expect(c.warning).toContain('4 min ago');
    expect(c.warning).toContain('do not rely');
    expect(c.hasPosition).toBe(true);
  });
  it('OFFLINE: state only, no age, no position', () => {
    const c = toCard(bus({ status: 'OFFLINE', latitude: null, longitude: null, ageSeconds: null }));
    expect([c.updatedText, c.hasPosition]).toEqual([null, false]);
    expect(c.warning).toContain('not sharing');
  });
  it('an OFFLINE bus never has a position even if coordinates slipped through', () => {
    expect(toCard(bus({ status: 'OFFLINE' })).hasPosition).toBe(false);
  });
});

describe('mapPoints: only buses with a position reach the map', () => {
  it('drops OFFLINE buses and buses without coordinates, keeps LIVE and STALE with their status', () => {
    const pts = mapPoints([
      bus(),
      bus({ vehiclePlate: 'B', status: 'STALE', latitude: 26.1, longitude: 80.1 }),
      bus({ vehiclePlate: 'C', status: 'OFFLINE', latitude: null, longitude: null }),
      bus({ vehiclePlate: 'D', status: 'OFFLINE' }),
      bus({ vehiclePlate: 'E', latitude: null }),
    ]);
    expect(pts.map((p) => [p.id, p.status])).toEqual([['UP32AB1234', 'LIVE'], ['B', 'STALE']]);
    expect(pts[0]).toEqual({ id: 'UP32AB1234', name: 'Ramesh Kumar', label: 'Bus 1', status: 'LIVE', lat: 26.85, lng: 80.95 });
  });
});

describe('buildUpdateScript', () => {
  it('calls __setBuses with JSON and returns true (injectJavaScript needs a value)', () => {
    const s = buildUpdateScript(mapPoints([bus()]));
    expect(s.startsWith('window.__setBuses && window.__setBuses([{')).toBe(true);
    expect(s.endsWith('); true;')).toBe(true);
  });
  it('a hostile driver name cannot break out of the call', () => {
    const evil = `x"});alert(1);//\u2028\u2029</script>`;
    const s = buildUpdateScript(mapPoints([bus({ driverName: evil, vehicleLabel: evil })]));
    expect(() => new Function(s)).not.toThrow();                                   // still one well-formed statement
    expect(s).not.toMatch(/[\u2028\u2029]/);                                       // the raw line separators are escaped
    const argument = s.slice('window.__setBuses && window.__setBuses('.length, s.length - '); true;'.length);
    expect(JSON.parse(argument)[0].name).toBe(evil);                               // and the name arrives intact, as data
  });
  it('an empty list clears the map', () => expect(buildUpdateScript([])).toBe('window.__setBuses && window.__setBuses([]); true;'));
});

describe('the map page (Leaflet inlined, no CDN)', () => {
  const page = buildMapPage({ tileUrl: MAP_CONFIG.tileUrl, attribution: MAP_CONFIG.attribution, center: MAP_CONFIG.defaultCenter, zoom: MAP_CONFIG.defaultZoom, maxZoom: MAP_CONFIG.maxZoom, focusZoom: MAP_CONFIG.focusZoom });

  it('loads no script, stylesheet or image from anywhere: Leaflet is part of the page', () => {
    expect(page).not.toMatch(/<script[^>]*\ssrc=/i);
    expect(page).not.toMatch(/<link[^>]*href=/i);
    expect(page).not.toMatch(/unpkg|jsdelivr|cdnjs|googleapis|cloudflare/i);
    expect(page).toContain('Leaflet 1.9.4');
  });
  it('uses the tile URL from the config and shows the OpenStreetMap attribution', () => {
    expect(page).toContain('https://tile.openstreetmap.org/{z}/{x}/{y}.png');
    expect(page).toContain('OpenStreetMap');
    expect(page).toContain('openstreetmap.org/copyright');
    expect(page).toContain('attributionControl: true');
  });
  it('exposes __setBuses, sets labels with textContent (never as HTML) and fits the view only once', () => {
    expect(page).toContain('window.__setBuses = function');
    expect(page).toContain('.textContent = p.label');
    const ours = page.slice(page.lastIndexOf('<script>'));                          // our script block (Leaflet's own code is the other block)
    expect(ours).not.toMatch(/innerHTML\s*=/);                                       // no assignment of data to innerHTML (reading holder.innerHTML is fine)
    expect(page).toContain('if (!fitted && points.length)');
  });
  it('every inline script is syntactically valid JavaScript', () => {
    const blocks = [...page.matchAll(/<script>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
    expect(blocks.length).toBe(2);
    for (const b of blocks) expect(() => new Function(b)).not.toThrow();
  });
  it('the Referer comes from baseUrl: the config has an https origin to give the WebView', () => {
    expect(MAP_CONFIG.baseUrl.startsWith('https://')).toBe(true);
    expect(MAP_CONFIG.pollSeconds).toBe(20);
  });
});
