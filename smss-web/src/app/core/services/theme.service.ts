import { Injectable, signal } from '@angular/core';
import { DEFAULT_THEME, THEMES, ThemeDefinition, ThemeName } from '../models';

/**
 * Applies a theme by toggling a `theme-*` class on <body>. All colors are
 * driven by CSS custom properties defined per theme in src/styles/themes,
 * so this service never touches individual component styles — changing
 * the class is enough for the entire application to re-skin instantly.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly themes: ThemeDefinition[] = THEMES;
  readonly activeTheme = signal<ThemeName>(DEFAULT_THEME);

  private lastClass: string | null = null;

  getThemeDefinition(name: ThemeName): ThemeDefinition {
    return this.themes.find((t) => t.name === name) ?? this.themes[0];
  }

  /** Apply a theme immediately (no reload) and update internal state. */
  applyTheme(name: ThemeName): void {
    const definition = this.getThemeDefinition(name);
    const body = document.body;

    if (this.lastClass) {
      body.classList.remove(this.lastClass);
    }
    body.classList.add(definition.className);
    this.lastClass = definition.className;
    this.activeTheme.set(definition.name);
  }
}
