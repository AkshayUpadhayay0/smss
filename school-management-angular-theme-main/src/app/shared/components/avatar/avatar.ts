import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-avatar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (src()) {
      <img class="avatar" [src]="src()" [alt]="name()" [style.width.px]="size()" [style.height.px]="size()" />
    } @else {
      <div class="avatar initials" [style.width.px]="size()" [style.height.px]="size()" [style.font-size.px]="size() * 0.4">
        {{ initials() }}
      </div>
    }
  `,
  styles: [`
    .avatar { border-radius: var(--radius-full); object-fit: cover; flex-shrink: 0; }
    .initials {
      display: flex; align-items: center; justify-content: center;
      background: var(--primary-light); color: var(--primary); font-weight: 700;
    }
  `],
})
export class AvatarComponent {
  src = input<string>('');
  name = input<string>('');
  size = input<number>(36);

  initials(): string {
    const parts = this.name().trim().split(' ');
    return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?';
  }
}
