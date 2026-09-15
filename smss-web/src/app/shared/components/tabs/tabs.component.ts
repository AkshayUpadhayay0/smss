import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';

export interface TabItem {
  id: string;
  label: string;
}

@Component({
  selector: 'app-tabs',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tabs.component.html',
  styleUrl: './tabs.component.scss',
})
export class TabsComponent {
  readonly tabs = input.required<TabItem[]>();
  readonly activeId = input<string | undefined>(undefined);
  readonly tabChange = output<string>();

  readonly internalActive = signal<string | null>(null);

  get active(): string {
    return this.activeId() ?? this.internalActive() ?? this.tabs()[0]?.id;
  }

  select(id: string): void {
    this.internalActive.set(id);
    this.tabChange.emit(id);
  }
}
