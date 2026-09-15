import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HeaderComponent } from './header/header.component';
import { SidebarComponent } from './sidebar/sidebar.component';
import { readStorage, STORAGE_KEYS, writeStorage } from '../../core/utils/storage.util';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [RouterOutlet, HeaderComponent, SidebarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './main-layout.component.html',
  styleUrl: './main-layout.component.scss',
})
export class MainLayoutComponent {
  readonly collapsed = signal(readStorage<boolean>(STORAGE_KEYS.sidebarCollapsed, false));
  readonly mobileOpen = signal(false);

  toggleSidebar(): void {
    this.collapsed.update((v) => !v);
    writeStorage(STORAGE_KEYS.sidebarCollapsed, this.collapsed());
  }

  toggleMobileSidebar(): void {
    this.mobileOpen.update((v) => !v);
  }

  closeMobileSidebar(): void {
    this.mobileOpen.set(false);
  }
}
