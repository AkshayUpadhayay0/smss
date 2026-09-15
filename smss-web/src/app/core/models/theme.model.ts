export type ThemeName = 'default' | 'green' | 'blue' | 'orange' | 'yellow' | 'purple';

export interface ThemeDefinition {
  name: ThemeName;
  label: string;
  /** CSS class applied to <body> to activate this theme */
  className: string;
  /** Swatch colors used by the theme preview UI */
  swatches: string[];
}

export const THEMES: ThemeDefinition[] = [
  {
    name: 'default',
    label: 'Default',
    className: 'theme-default',
    swatches: ['#4f46e5', '#16a34a', '#0284c7', '#d97706'],
  },
  {
    name: 'green',
    label: 'Green',
    className: 'theme-green',
    swatches: ['#15803d', '#4ade80', '#0284c7', '#d97706'],
  },
  {
    name: 'blue',
    label: 'Blue',
    className: 'theme-blue',
    swatches: ['#2563eb', '#60a5fa', '#16a34a', '#d97706'],
  },
  {
    name: 'orange',
    label: 'Orange',
    className: 'theme-orange',
    swatches: ['#ea580c', '#fb923c', '#16a34a', '#0284c7'],
  },
  {
    name: 'yellow',
    label: 'Yellow',
    className: 'theme-yellow',
    swatches: ['#d97706', '#facc15', '#16a34a', '#0284c7'],
  },
  {
    name: 'purple',
    label: 'Purple',
    className: 'theme-purple',
    swatches: ['#7e22ce', '#c084fc', '#16a34a', '#d97706'],
  },
];

export const DEFAULT_THEME: ThemeName = 'default';
