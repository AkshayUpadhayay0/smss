import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { ThemeService, COLOR_PRESETS, ColorPreset, SidebarStyle, ThemeMode } from '../../core/services/theme.service';
import { BrandingService } from '../../core/services/branding.service';

@Component({
  selector: 'app-settings-page',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, TabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings-page.html',
})
export class SettingsPageComponent {
  theme = inject(ThemeService);
  branding = inject(BrandingService);
  presets = COLOR_PRESETS;

  activeTab = signal('appearance');
  saved = signal(false);

  tabs: TabItem[] = [
    { id: 'general', label: 'General', icon: 'settings' },
    { id: 'academic', label: 'Academic', icon: 'book' },
    { id: 'appearance', label: 'Appearance', icon: 'palette' },
    { id: 'notifications', label: 'Notifications', icon: 'bell' },
    { id: 'security', label: 'Security', icon: 'shield' },
  ];

  schoolNameInput = signal(this.branding.branding().name);
  taglineInput = signal(this.branding.branding().tagline);

  selectPreset(id: ColorPreset): void {
    this.theme.setPreset(id);
  }

  onCustomColor(value: string): void {
    this.theme.setCustomColor(value);
  }

  setMode(mode: ThemeMode): void {
    this.theme.setMode(mode);
  }

  setSidebarStyle(style: SidebarStyle): void {
    this.theme.setSidebarStyle(style);
  }

  saveGeneral(): void {
    this.branding.update({ name: this.schoolNameInput(), tagline: this.taglineInput() });
    this.flashSaved();
  }

  flashSaved(): void {
    this.saved.set(true);
    setTimeout(() => this.saved.set(false), 2000);
  }
}
