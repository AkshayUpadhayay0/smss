import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { Router } from '@angular/router';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { AvatarComponent } from '../../../shared/components/avatar/avatar.component';
import { DropdownComponent } from '../../../shared/components/dropdown/dropdown.component';
import { AuthService, ToastService } from '../../../core/services';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [IconComponent, AvatarComponent, DropdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss',
})
export class HeaderComponent {
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  readonly authService = inject(AuthService);

  readonly toggleSidebar = output<void>();
  readonly toggleMobileSidebar = output<void>();

  goToChangePassword(): void {
    this.router.navigate(['/change-password']);
  }

  logout(): void {
    this.authService.logout();
    this.toastService.info('Signed out', 'You have been logged out successfully.');
    this.router.navigate(['/login']);
  }
}
