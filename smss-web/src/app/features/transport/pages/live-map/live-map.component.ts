import { isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  InjectionToken,
  PLATFORM_ID,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import type * as Leaflet from 'leaflet';
import { MAP_CONFIG } from '../../../../core/config/map.config';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { DutyStatus, LiveBoard, LiveDriver, escapeHtml, formatAge, liveLabel } from '../../models/transport.model';
import { TransportService } from '../../services/transport.service';

export type LeafletLib = typeof Leaflet;

/** Leaflet touches `window` when it is imported, so it is loaded in the browser only, on demand. Tests provide a stand-in through this token. */
export const LEAFLET_LOADER = new InjectionToken<() => Promise<LeafletLib>>('LEAFLET_LOADER', {
  providedIn: 'root',
  factory: () => async () => {
    const mod = (await import('leaflet')) as unknown as { default?: LeafletLib } & LeafletLib;
    return mod.default ?? mod;
  },
});

const STATUS_TEXT: Record<DutyStatus, string> = { LIVE: 'Live', STALE: 'Stale', OFFLINE: 'Offline' };

/**
 * Where every driver on duty is right now (School Admin and Principal). The server decides LIVE / STALE / OFFLINE from the time the last fix ARRIVED.
 * The page asks for the board every pollSeconds while the tab is visible, and updates markers in place (nothing is re-created, the view is not reset).
 */
@Component({
  selector: 'app-live-map',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './live-map.component.scss',
  templateUrl: './live-map.component.html',
})
export class LiveMapComponent {
  private readonly service = inject(TransportService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly loadLeaflet = inject(LEAFLET_LOADER);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  protected readonly breadcrumbs = [{ label: 'Transport' }, { label: 'Live map' }];
  protected readonly mapEl = viewChild.required<ElementRef<HTMLDivElement>>('map');

  protected readonly board = signal<LiveBoard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly mapReady = signal(false);
  protected readonly mapFailed = signal(false);
  /** When the board was fetched (browser clock): only used to age "updated X ago" between polls. */
  private readonly fetchedAt = signal(Date.now());
  private readonly clock = signal(Date.now());

  protected readonly items = computed(() => this.board()?.items ?? []);
  protected readonly counts = computed(() => this.board()?.counts ?? { live: 0, stale: 0, offline: 0 });
  protected readonly withPosition = computed(() => this.items().filter((i) => i.hasFix && i.latitude != null && i.longitude != null));
  protected readonly waitingForFix = computed(() => this.items().length > 0 && this.withPosition().length === 0);

  private L: LeafletLib | null = null;
  private map: Leaflet.Map | null = null;
  private readonly markers = new Map<string, Leaflet.Marker>();
  private fitted = false;
  private inFlight = false;
  private timer: ReturnType<typeof setInterval> | null = null;
  private clockTimer: ReturnType<typeof setInterval> | null = null;
  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    if (!this.isBrowser) return;
    afterNextRender(() => void this.start());
    this.destroyRef.onDestroy(() => this.stop());
  }

  private async start(): Promise<void> {
    document.addEventListener('visibilitychange', this.onVisibility);
    this.timer = setInterval(() => this.poll(), MAP_CONFIG.pollSeconds * 1000);
    this.clockTimer = setInterval(() => this.clock.set(Date.now()), 5000);
    this.load(); // do not wait for the map library: the list can show at once
    try {
      this.L = await this.loadLeaflet();
      this.createMap(this.L);
    } catch {
      this.mapFailed.set(true);
    }
  }

  private stop(): void {
    document.removeEventListener('visibilitychange', this.onVisibility);
    if (this.timer) clearInterval(this.timer);
    if (this.clockTimer) clearInterval(this.clockTimer);
    this.resizeObserver?.disconnect();
    this.map?.remove();
    this.map = null;
    this.markers.clear();
  }

  // ------------------------------------------------------------------ polling (paused while the tab is hidden)
  private readonly onVisibility = (): void => {
    if (!document.hidden) this.load(); // back on the tab: refresh at once instead of waiting for the next tick
  };

  private poll(): void {
    if (!document.hidden) this.load();
  }

  protected load(): void {
    if (this.inFlight) return;
    this.inFlight = true;
    this.service.live().subscribe({
      next: (board) => {
        this.inFlight = false;
        this.loading.set(false);
        this.loadFailed.set(false);
        this.fetchedAt.set(Date.now());
        this.clock.set(Date.now());
        this.board.set(board);
        this.syncMarkers();
      },
      error: () => {
        this.inFlight = false;
        this.loading.set(false);
        this.loadFailed.set(true); // keep showing the last board; the banner offers a retry
      },
    });
  }

  // ------------------------------------------------------------------ map
  private createMap(L: LeafletLib): void {
    const el = this.mapEl().nativeElement;
    this.map = L.map(el, { zoomControl: true, attributionControl: true }).setView(MAP_CONFIG.defaultCenter, MAP_CONFIG.defaultZoom);
    L.tileLayer(MAP_CONFIG.tileUrl, { maxZoom: MAP_CONFIG.maxZoom, attribution: MAP_CONFIG.attribution }).addTo(this.map);
    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(() => this.map?.invalidateSize());
      this.resizeObserver.observe(el);
    }
    this.mapReady.set(true);
    this.syncMarkers();
  }

  private icon(d: LiveDriver, selected: boolean): Leaflet.DivIcon {
    const cls = `pin pin--${d.status.toLowerCase()}${selected ? ' pin--selected' : ''}`;
    return this.L!.divIcon({
      className: 'live-pin',
      html: `<div class="${cls}"><span class="pin__dot" aria-hidden="true"></span><span class="pin__text">${escapeHtml(liveLabel(d))}</span><span class="pin__state">${STATUS_TEXT[d.status]}</span></div>`,
      iconSize: undefined,
      iconAnchor: [14, 14],
    });
  }

  /** Brings the markers in line with the board: moves and restyles existing ones, adds new ones, removes the ones that are gone. Never re-creates the map or resets the view. */
  private syncMarkers(): void {
    const L = this.L;
    const map = this.map;
    if (!L || !map) return;

    const wanted = new Map(this.withPosition().map((d) => [d.driverId, d]));
    for (const [id, marker] of this.markers) {
      if (!wanted.has(id)) {
        marker.remove();
        this.markers.delete(id);
      }
    }
    for (const [id, d] of wanted) {
      const latlng: Leaflet.LatLngExpression = [d.latitude!, d.longitude!];
      const selected = id === this.selectedId();
      const existing = this.markers.get(id);
      if (existing) {
        existing.setLatLng(latlng);
        existing.setIcon(this.icon(d, selected));
      } else {
        const marker = L.marker(latlng, { icon: this.icon(d, selected), title: liveLabel(d), keyboard: true });
        marker.on('click', () => this.select(id, false));
        marker.addTo(map);
        this.markers.set(id, marker);
      }
    }

    // fit to the markers ONCE, the first time there is something to show; after that the user's pan and zoom are never overridden
    if (!this.fitted && wanted.size > 0) {
      this.fitted = true;
      const points = [...wanted.values()].map((d) => [d.latitude!, d.longitude!] as Leaflet.LatLngTuple);
      if (points.length === 1) map.setView(points[0], MAP_CONFIG.focusZoom);
      else map.fitBounds(L.latLngBounds(points), { padding: [48, 48], maxZoom: MAP_CONFIG.focusZoom });
    }
  }

  // ------------------------------------------------------------------ list
  protected select(driverId: string, flyThere = true): void {
    const previous = this.selectedId();
    this.selectedId.set(driverId);
    const d = this.items().find((i) => i.driverId === driverId);
    if (!d) return;
    for (const id of [previous, driverId]) {
      const item = this.items().find((i) => i.driverId === id);
      const marker = id ? this.markers.get(id) : undefined;
      if (item && marker) marker.setIcon(this.icon(item, id === driverId));
    }
    if (flyThere && this.map && d.latitude != null && d.longitude != null) {
      this.map.flyTo([d.latitude, d.longitude], Math.max(this.map.getZoom(), MAP_CONFIG.focusZoom));
    }
  }

  protected statusText(s: DutyStatus): string {
    return STATUS_TEXT[s];
  }

  /** "updated X ago": the age the server reported, plus the time passed since this board was fetched. */
  protected updated(d: LiveDriver): string {
    if (d.ageSeconds == null) return formatAge(null);
    return `updated ${formatAge(d.ageSeconds + Math.max(0, (this.clock() - this.fetchedAt()) / 1000))}`;
  }

  protected label(d: LiveDriver): string {
    return liveLabel(d);
  }
}
