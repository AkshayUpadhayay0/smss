import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { IconComponent } from '../icon/icon.component';

export interface AccordionItem {
  id: string;
  title: string;
  content: string;
}

@Component({
  selector: 'app-accordion',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './accordion.component.html',
  styleUrl: './accordion.component.scss',
})
export class AccordionComponent {
  readonly items = input.required<AccordionItem[]>();
  readonly allowMultiple = input<boolean>(false);

  readonly openIds = signal<Set<string>>(new Set());

  isOpen(id: string): boolean {
    return this.openIds().has(id);
  }

  toggle(id: string): void {
    const current = new Set(this.openIds());
    if (current.has(id)) {
      current.delete(id);
    } else {
      if (!this.allowMultiple()) current.clear();
      current.add(id);
    }
    this.openIds.set(current);
  }
}
