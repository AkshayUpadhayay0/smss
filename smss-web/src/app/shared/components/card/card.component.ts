import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './card.component.scss',
  template: `
    <section class="card">
      @if (title()) {
        <header class="card-header"><h3>{{ title() }}</h3></header>
      }
      <div class="card-body" [class.no-padding]="noPadding()">
        <ng-content />
      </div>
    </section>
  `,
})
export class CardComponent {
  readonly title = input<string>();
  readonly noPadding = input(false);
}
