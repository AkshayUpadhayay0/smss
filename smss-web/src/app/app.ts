import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastHostComponent } from './shared/components/toast/toast-host.component';
import { ConfirmDialogComponent } from './shared/components/confirm-dialog/confirm-dialog.component';
import { PageLoaderComponent } from './shared/components/loader/loader.component';
import { SeedService } from './core/services/seed.service';
import { AuthService } from './core/auth/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, ToastHostComponent, ConfirmDialogComponent, PageLoaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  // Order matters: seed the mock "database" before AuthService restores/
  // validates a session against it.
  private readonly seedService = inject(SeedService);
  private readonly authService = inject(AuthService);

  constructor() {
    this.seedService.seedIfNeeded();
  }
}
