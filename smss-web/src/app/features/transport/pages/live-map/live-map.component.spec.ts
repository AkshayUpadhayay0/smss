import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { MAP_CONFIG } from '../../../../core/config/map.config';
import { escapeHtml, formatAge, liveLabel } from '../../models/transport.model';
import { LEAFLET_LOADER, LiveMapComponent } from './live-map.component';

const API = environment.apiUrl;
const URL = `${API}/api/Transport/live`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const driver = (id: string, name: string, status: 'LIVE' | 'STALE' | 'OFFLINE', lat: number | null, lng: number | null, extra: Record<string, unknown> = {}) => ({
  driverId: id, driverName: name, vehicleId: 1, vehicleLabel: 'Bus 1', vehiclePlate: 'UP32AB1234', areas: ['Civil Lines'], hasFix: lat != null,
  latitude: lat, longitude: lng, accuracyMeters: 8, speedKmh: 20, headingDegrees: 90, status, updatedAt: '2026-11-13T10:00:00Z', ageSeconds: lat == null ? null : 12,
  sessionStartedAt: '2026-11-13T09:00:00Z', ...extra,
});
const board = (...items: ReturnType<typeof driver>[]) => ({
  serverTime: '2026-11-13T10:00:12Z',
  counts: { live: items.filter((i) => i.status === 'LIVE').length, stale: items.filter((i) => i.status === 'STALE').length, offline: items.filter((i) => i.status === 'OFFLINE').length },
  items,
});

// ---- a stand-in for Leaflet that records what the page does with it
class FakeMarker {
  setLatLngCalls = 0;
  removed = false;
  handlers: Record<string, () => void> = {};
  constructor(public latlng: [number, number], public options: { icon: { html: string }; title: string }) {}
  get icon() { return this.options.icon; }
  setLatLng(l: [number, number]) { this.latlng = l; this.setLatLngCalls++; return this; }
  setIcon(i: { html: string }) { this.options.icon = i; return this; }
  on(e: string, f: () => void) { this.handlers[e] = f; return this; }
  addTo(m: FakeMap) { m.markers.push(this); return this; }
  remove() { this.removed = true; return this; }
}
class FakeMap {
  markers: FakeMarker[] = [];
  setView = vi.fn(() => this);
  fitBounds = vi.fn();
  flyTo = vi.fn();
  getZoom = () => 10;
  invalidateSize = vi.fn();
  remove = vi.fn();
}

describe('LiveMapComponent', () => {
  let fixture: ComponentFixture<LiveMapComponent>;
  let http: HttpTestingController;
  let map: FakeMap;
  let mapsCreated: number;
  let tileUrl: string | undefined;
  let hidden = false;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as { load(): void; select(id: string): void };
  const text = () => el().textContent ?? '';
  const live = () => map.markers.filter((m) => !m.removed);

  const lib = () => ({
    map: () => { mapsCreated++; return map; },
    tileLayer: (url: string) => { tileUrl = url; return { addTo: vi.fn() }; },
    marker: (ll: [number, number], o: { icon: { html: string }; title: string }) => new FakeMarker(ll, o),
    divIcon: (o: { html: string }) => o,
    latLngBounds: (pts: unknown) => pts,
  });

  async function open(loader: () => Promise<unknown> = async () => lib()) {
    vi.useFakeTimers();
    map = new FakeMap();
    mapsCreated = 0;
    hidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: LEAFLET_LOADER, useValue: loader }] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LiveMapComponent);
    fixture.detectChanges();
    TestBed.inject(ApplicationRef).tick();   // runs afterNextRender: starts polling and loads the map library
    await vi.advanceTimersByTimeAsync(0);
  }
  const answer = async (b: ReturnType<typeof board>) => {
    http.expectOne(URL).flush(env(b));
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
  };

  afterEach(() => {
    fixture?.destroy();
    http?.verify();
    vi.useRealTimers();
    Reflect.deleteProperty(document, 'hidden');
  });

  it('loads the board at once, shows each driver with a status WORD, the counts and "updated X ago"', async () => {
    await open();
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95), driver('D2', 'Suresh Yadav', 'STALE', 26.86, 80.96), driver('D3', 'Anil Singh', 'OFFLINE', 26.87, 80.97, { ageSeconds: 700 })));
    expect(text()).toContain('Ramesh Kumar');
    expect(text()).toContain('Bus 1 · UP32AB1234');
    expect(text()).toContain('Civil Lines');
    for (const word of ['Live 1', 'Stale 1', 'Offline 1']) expect(text()).toContain(word);
    expect(text()).toContain('updated 12 s ago');
    expect(text()).toContain('updated 11 min ago');
  });

  it('creates the map once with the tile URL from map.config and one pill per driver that has a position', async () => {
    await open();
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95), driver('D2', 'No Fix Yet', 'OFFLINE', null, null)));
    expect(mapsCreated).toBe(1);
    expect(tileUrl).toBe(MAP_CONFIG.tileUrl);
    expect(live().length).toBe(1);
    expect(live()[0].icon.html).toContain('Ramesh Kumar · Bus 1');
    expect(live()[0].icon.html).toContain('Live');                      // a text label, not colour alone
    expect(text()).toContain('no location yet');                       // the driver without a fix is in the list only
    expect(el().querySelectorAll('button.row')[1].hasAttribute('disabled')).toBe(true);
  });

  it('escapes names that go into the marker HTML', async () => {
    await open();
    await answer(board(driver('D1', '<img src=x onerror=alert(1)>', 'LIVE', 26.85, 80.95)));
    expect(live()[0].icon.html).not.toContain('<img');
    expect(live()[0].icon.html).toContain('&lt;img');
  });

  it('fits the view to the markers only on the FIRST load; later polls move markers in place and never touch the view', async () => {
    await open();
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95), driver('D2', 'Suresh Yadav', 'LIVE', 26.9, 81.0)));
    expect(map.fitBounds).toHaveBeenCalledTimes(1);
    const first = live()[0];

    await vi.advanceTimersByTimeAsync(MAP_CONFIG.pollSeconds * 1000);
    await answer(board(driver('D1', 'Ramesh Kumar', 'STALE', 26.851, 80.951), driver('D2', 'Suresh Yadav', 'LIVE', 26.91, 81.01), driver('D3', 'New Driver', 'LIVE', 27.0, 81.2)));
    expect(live()[0]).toBe(first);                                     // the same marker object, updated in place
    expect(first.setLatLngCalls).toBe(1);
    expect(first.latlng).toEqual([26.851, 80.951]);
    expect(first.icon.html).toContain('Stale');                        // restyled by status
    expect(live().length).toBe(3);                                     // the new driver got a marker
    expect(mapsCreated).toBe(1);
    expect(map.fitBounds).toHaveBeenCalledTimes(1);                    // the user's pan / zoom is not overridden
    expect(map.setView).toHaveBeenCalledTimes(1);                      // only the initial default view
  });

  it('removes the marker of a driver who is no longer on the board', async () => {
    await open();
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95), driver('D2', 'Suresh Yadav', 'LIVE', 26.9, 81.0)));
    await vi.advanceTimersByTimeAsync(MAP_CONFIG.pollSeconds * 1000);
    await answer(board(driver('D2', 'Suresh Yadav', 'LIVE', 26.9, 81.0)));
    expect(map.markers.filter((m) => m.removed).length).toBe(1);
    expect(live().length).toBe(1);
    expect(text()).not.toContain('Ramesh Kumar');
  });

  it('polls every pollSeconds, PAUSES while the tab is hidden, and refreshes at once when it is shown again', async () => {
    await open();
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95)));

    await vi.advanceTimersByTimeAsync(MAP_CONFIG.pollSeconds * 1000 - 1);
    http.expectNone(URL);
    await vi.advanceTimersByTimeAsync(1);
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95)));        // one poll per interval

    hidden = true;
    await vi.advanceTimersByTimeAsync(MAP_CONFIG.pollSeconds * 1000 * 3);
    http.expectNone(URL);                                                            // hidden: nothing is requested

    hidden = false;
    document.dispatchEvent(new Event('visibilitychange'));
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.86, 80.96)));        // visible again: an immediate refresh
    expect(live()[0].latlng).toEqual([26.86, 80.96]);
  });

  it('does not start a second request while one is in flight, and stops polling when the page is left', async () => {
    await open();
    http.expectOne(URL);                                                             // the first request is still pending
    await vi.advanceTimersByTimeAsync(MAP_CONFIG.pollSeconds * 1000);
    http.expectNone(URL);                                                            // no overlap
    fixture.destroy();
    await vi.advanceTimersByTimeAsync(MAP_CONFIG.pollSeconds * 1000 * 2);
    http.expectNone(URL);                                                            // no timer left behind
    expect(map.remove).toHaveBeenCalled();
  });

  it('shows the empty state when nobody is on duty', async () => {
    await open();
    await answer(board());
    expect(text()).toContain('No drivers on duty');
    expect(live().length).toBe(0);
  });

  it('says so when the board cannot be loaded, offers Try again, and keeps the last positions when a later refresh fails', async () => {
    await open();
    http.expectOne(URL).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'err' });
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(text()).toContain('Could not load the live map.');
    expect(text()).not.toContain('No drivers on duty');                              // an error is not "nobody on duty"
    cmp().load();
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95)));
    expect(text()).not.toContain('Could not');

    await vi.advanceTimersByTimeAsync(MAP_CONFIG.pollSeconds * 1000);
    http.expectOne(URL).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'err' });
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(text()).toContain('Showing the last known positions');
    expect(text()).toContain('Ramesh Kumar');
    expect(live().length).toBe(1);
  });

  it('picking a driver in the list flies to the marker and highlights it; a driver without a fix cannot be picked', async () => {
    await open();
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95), driver('D2', 'No Fix', 'OFFLINE', null, null)));
    (el().querySelectorAll('button.row')[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(map.flyTo).toHaveBeenCalledWith([26.85, 80.95], MAP_CONFIG.focusZoom);
    expect(live()[0].icon.html).toContain('pin--selected');
    expect(el().querySelectorAll('button.row')[0].classList.contains('row--selected')).toBe(true);
  });

  it('clicking a marker selects its row without moving the map', async () => {
    await open();
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95)));
    live()[0].handlers['click']();
    fixture.detectChanges();
    expect(el().querySelector('button.row')!.classList.contains('row--selected')).toBe(true);
    expect(map.flyTo).not.toHaveBeenCalled();
  });

  it('still lists the drivers when the map library cannot be loaded', async () => {
    await open(() => Promise.reject(new Error('offline')));
    await answer(board(driver('D1', 'Ramesh Kumar', 'LIVE', 26.85, 80.95)));
    expect(text()).toContain('The map could not be loaded');
    expect(text()).toContain('Ramesh Kumar');
  });

  it('tells the user when drivers are on duty but none has reported a position yet', async () => {
    await open();
    await answer(board(driver('D1', 'Ramesh Kumar', 'OFFLINE', null, null)));
    expect(text()).toContain('Waiting for the first location');
  });
});

describe('live map helpers', () => {
  it('formatAge', () => {
    expect(formatAge(null)).toBe('no location yet');
    expect(formatAge(3)).toBe('just now');
    expect(formatAge(45)).toBe('45 s ago');
    expect(formatAge(125)).toBe('2 min ago');
    expect(formatAge(7300)).toBe('2 h ago');
  });
  it('escapeHtml and liveLabel', () => {
    expect(escapeHtml(`<a href="x">'&'</a>`)).toBe('&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt;');
    expect(liveLabel({ driverName: 'Ramesh', vehicleLabel: 'Bus 1' })).toBe('Ramesh · Bus 1');
  });
});
