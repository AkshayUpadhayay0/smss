import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-add-school',
  standalone: true,
  imports: [ ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './add-school.component.html',
  styleUrl: './add-school.component.scss',
})
export class AddSchoolComponent {
    private readonly router = inject(Router);
    
}