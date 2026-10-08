import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LogoComponent } from '../../shared/components/logo/logo.component';

@Component({
  selector: 'app-auth-layout',
  imports: [RouterOutlet, LogoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-layout.component.scss',
  template: `
    <div class="wrap">
      <div class="brand"><app-logo /></div>
      <div class="card">
        <router-outlet />
      </div>
      <p class="footer">School Management System</p>
    </div>
  `,
})
export class AuthLayoutComponent {}
