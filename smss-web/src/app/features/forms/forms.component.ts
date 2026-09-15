import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../shared/components/card/card.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { ButtonComponent } from '../../shared/components/button/button.component';
import { InputComponent } from '../../shared/components/input/input.component';
import { SelectComponent, SelectOption } from '../../shared/components/select/select.component';
import { TextareaComponent } from '../../shared/components/textarea/textarea.component';
import { CheckboxComponent } from '../../shared/components/checkbox/checkbox.component';
import { RadioComponent, RadioOption } from '../../shared/components/radio/radio.component';
import { SwitchComponent } from '../../shared/components/switch/switch.component';
import { ToastService } from '../../core/services';

const ROLE_OPTIONS: SelectOption[] = [
  { label: 'Administrator', value: 'admin' },
  { label: 'Manager', value: 'manager' },
  { label: 'Staff', value: 'staff' },
  { label: 'Viewer', value: 'viewer' },
];

const SKILL_OPTIONS: SelectOption[] = [
  { label: 'Angular', value: 'angular' },
  { label: 'TypeScript', value: 'typescript' },
  { label: 'SCSS', value: 'scss' },
  { label: 'Node.js', value: 'node' },
  { label: 'PostgreSQL', value: 'postgres' },
];

const PLAN_OPTIONS: RadioOption[] = [
  { label: 'Starter', value: 'starter' },
  { label: 'Growth', value: 'growth' },
  { label: 'Enterprise', value: 'enterprise' },
];

@Component({
  selector: 'app-forms-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    CardComponent,
    IconComponent,
    ButtonComponent,
    InputComponent,
    SelectComponent,
    TextareaComponent,
    CheckboxComponent,
    RadioComponent,
    SwitchComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './forms.component.html',
  styleUrl: './forms.component.scss',
})
export class FormsPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly toastService = inject(ToastService);

  readonly roleOptions = ROLE_OPTIONS;
  readonly skillOptions = SKILL_OPTIONS;
  readonly planOptions = PLAN_OPTIONS;

  readonly form = this.fb.group({
    // Text fields
    fullName: ['', [Validators.required, Validators.minLength(2)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    age: [null as number | null, [Validators.min(1), Validators.max(120)]],
    website: [''],
    phone: [''],

    // Selection controls
    role: ['', Validators.required],
    skills: [[] as string[]],
    plan: ['growth'],
    isActive: [true],
    receiveUpdates: [false],

    // Other controls
    startDate: [''],
    endDate: [''],
    meetingTime: [''],
    brandColor: ['#4f46e5'],
    bio: [''],
    experienceYears: [5],

    // Disabled / readonly demo
    organizationCode: [{ value: 'DEMO', disabled: true }],
    accountId: [{ value: 'ACC-0001', disabled: false }],
  });

  get f() {
    return this.form.controls;
  }

  errorFor(controlName: keyof typeof this.form.controls): string | undefined {
    const control = this.form.get(controlName as string);
    if (!control || !control.touched || control.valid) return undefined;

    if (control.hasError('required')) return 'This field is required.';
    if (control.hasError('email')) return 'Please enter a valid email address.';
    if (control.hasError('minlength')) {
      const req = control.getError('minlength').requiredLength;
      return `Must be at least ${req} characters.`;
    }
    if (control.hasError('min')) return `Value is too low.`;
    if (control.hasError('max')) return `Value is too high.`;
    return 'This field is invalid.';
  }

  onSubmit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.toastService.danger('Please fix the errors', 'Some fields need your attention before submitting.');
      return;
    }
    this.toastService.success('Form submitted', 'All fields validated successfully (mock submission).');
  }

  onReset(): void {
    this.form.reset({
      fullName: '',
      email: '',
      password: '',
      age: null,
      website: '',
      phone: '',
      role: '',
      skills: [],
      plan: 'growth',
      isActive: true,
      receiveUpdates: false,
      startDate: '',
      endDate: '',
      meetingTime: '',
      brandColor: '#4f46e5',
      bio: '',
      experienceYears: 5,
      accountId: 'ACC-0001',
    });
  }
}
