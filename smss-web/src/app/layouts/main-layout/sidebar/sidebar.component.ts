import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { NAV_ITEMS } from '../../../core/utils/nav.util';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {
  readonly collapsed = input<boolean>(false);
  readonly mobileOpen = input<boolean>(false);
  readonly closeMobile = output<void>();

  readonly navItems = NAV_ITEMS;
  readonly expandedGroups = signal<Set<string>>(new Set(NAV_ITEMS.filter((i) => i.children).map((i) => i.label)));

  isExpanded(label: string): boolean {
    return this.expandedGroups().has(label);
  }

  toggleGroup(label: string): void {
    const next = new Set(this.expandedGroups());
    if (next.has(label)) {
      next.delete(label);
    } else {
      next.add(label);
    }
    this.expandedGroups.set(next);
  }

  onLinkClick(): void {
    this.closeMobile.emit();
  }
}
