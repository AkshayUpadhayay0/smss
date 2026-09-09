import { Injectable, effect, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark' | 'system';
export type ColorPreset = 'blue' | 'indigo' | 'purple' | 'green' | 'teal' | 'orange' | 'red' | 'custom';
export type SidebarStyle = 'expanded' | 'collapsed' | 'icon';

export interface PresetDef {
  id: ColorPreset;
  name: string;
  primary: string;
  primaryHover: string;
  primaryLight: string;
}

export const COLOR_PRESETS: PresetDef[] = [
  { id: 'blue', name: 'Blue', primary: '#2563eb', primaryHover: '#1d4ed8', primaryLight: '#dbeafe' },
  { id: 'indigo', name: 'Indigo', primary: '#4f46e5', primaryHover: '#4338ca', primaryLight: '#e0e7ff' },
  { id: 'purple', name: 'Purple', primary: '#7c3aed', primaryHover: '#6d28d9', primaryLight: '#ede9fe' },
  { id: 'green', name: 'Green', primary: '#059669', primaryHover: '#047857', primaryLight: '#d1fae5' },
  { id: 'teal', name: 'Teal', primary: '#0d9488', primaryHover: '#0f766e', primaryLight: '#ccfbf1' },
  { id: 'orange', name: 'Orange', primary: '#ea580c', primaryHover: '#c2410c', primaryLight: '#ffedd5' },
  { id: 'red', name: 'Red', primary: '#dc2626', primaryHover: '#b91c1c', primaryLight: '#fee2e2' },
];

interface ThemeSettings {
  mode: ThemeMode;
  preset: ColorPreset;
  customColor: string;
  sidebarStyle: SidebarStyle;
  compact: boolean;
}

const STORAGE_KEY = 'smt-theme-settings';

function hexToRgbTriplet(hex: string): string {
  const clean = hex.replace('#', '');
  const bigint = parseInt(clean.length === 3
    ? clean.split('').map(c => c + c).join('')
    : clean, 16);
  const r = (bigint >> 16) & 255;
  const g = (bigint >> 8) & 255;
  const b = bigint & 255;
  return `${r} ${g} ${b}`;
}

function shade(hex: string, percent: number): string {
  const clean = hex.replace('#', '');
  const num = parseInt(clean, 16);
  let r = (num >> 16) + Math.round(255 * percent);
  let g = ((num >> 8) & 0x00ff) + Math.round(255 * percent);
  let b = (num & 0x0000ff) + Math.round(255 * percent);
  r = Math.min(255, Math.max(0, r));
  g = Math.min(255, Math.max(0, g));
  b = Math.min(255, Math.max(0, b));
  return `#${((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1)}`;
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly defaults: ThemeSettings = {
    mode: 'light',
    preset: 'indigo',
    customColor: '#4f46e5',
    sidebarStyle: 'expanded',
    compact: false,
  };

  readonly mode = signal<ThemeMode>(this.defaults.mode);
  readonly preset = signal<ColorPreset>(this.defaults.preset);
  readonly customColor = signal<string>(this.defaults.customColor);
  readonly sidebarStyle = signal<SidebarStyle>(this.defaults.sidebarStyle);
  readonly compact = signal<boolean>(this.defaults.compact);
  readonly resolvedDark = signal<boolean>(false);
  readonly mobileSidebarOpen = signal<boolean>(false);

  private mediaQuery = typeof window !== 'undefined' ? window.matchMedia('(prefers-color-scheme: dark)') : null;

  constructor() {
    this.load();
    this.mediaQuery?.addEventListener('change', () => this.applyMode());

    effect(() => {
      this.applyMode();
      // eslint-disable-next-line
      this.mode();
    });

    effect(() => {
      const p = this.preset();
      const custom = this.customColor();
      const def = COLOR_PRESETS.find(c => c.id === p);
      const primary = p === 'custom' ? custom : (def?.primary ?? this.defaults.customColor);
      const hover = p === 'custom' ? shade(custom, -0.12) : (def?.primaryHover ?? primary);
      const light = p === 'custom' ? shade(custom, 0.82) : (def?.primaryLight ?? primary);
      this.setCssVar('--primary', primary);
      this.setCssVar('--primary-hover', hover);
      this.setCssVar('--primary-light', light);
      this.setCssVar('--primary-rgb', hexToRgbTriplet(primary));
      this.persist();
    });

    effect(() => {
      this.setCssVar('--sidebar-width', this.sidebarStyle() === 'icon' ? '80px' : this.sidebarStyle() === 'collapsed' ? '0px' : '272px');
      this.persist();
    });

    effect(() => {
      document.documentElement.classList.toggle('compact', this.compact());
      this.persist();
    });
  }

  private setCssVar(name: string, value: string): void {
    if (typeof document !== 'undefined') {
      document.documentElement.style.setProperty(name, value);
    }
  }

  private applyMode(): void {
    const mode = this.mode();
    const dark = mode === 'dark' || (mode === 'system' && !!this.mediaQuery?.matches);
    this.resolvedDark.set(dark);
    if (typeof document !== 'undefined') {
      document.documentElement.classList.toggle('dark', dark);
      document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
    }
  }

  setMode(mode: ThemeMode): void { this.mode.set(mode); this.persist(); }
  setPreset(preset: ColorPreset): void { this.preset.set(preset); }
  setCustomColor(color: string): void { this.customColor.set(color); this.preset.set('custom'); }
  setSidebarStyle(style: SidebarStyle): void { this.sidebarStyle.set(style); }
  toggleCompact(): void { this.compact.set(!this.compact()); }
  toggleMobileSidebar(): void { this.mobileSidebarOpen.set(!this.mobileSidebarOpen()); }
  closeMobileSidebar(): void { this.mobileSidebarOpen.set(false); }

  resetToDefaults(): void {
    this.mode.set(this.defaults.mode);
    this.preset.set(this.defaults.preset);
    this.customColor.set(this.defaults.customColor);
    this.sidebarStyle.set(this.defaults.sidebarStyle);
    this.compact.set(false);
  }

  private persist(): void {
    if (typeof localStorage === 'undefined') return;
    const settings: ThemeSettings = {
      mode: this.mode(), preset: this.preset(), customColor: this.customColor(),
      sidebarStyle: this.sidebarStyle(), compact: this.compact(),
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  }

  private load(): void {
    if (typeof localStorage === 'undefined') return;
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return;
      const parsed: Partial<ThemeSettings> = JSON.parse(raw);
      if (parsed.mode) this.mode.set(parsed.mode);
      if (parsed.preset) this.preset.set(parsed.preset);
      if (parsed.customColor) this.customColor.set(parsed.customColor);
      if (parsed.sidebarStyle) this.sidebarStyle.set(parsed.sidebarStyle);
      if (typeof parsed.compact === 'boolean') this.compact.set(parsed.compact);
    } catch { /* ignore malformed storage */ }
  }
}
