import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StudentService, ClassService } from '../../core/services/data.service';
import { SchoolClass } from '../../core/models/school.models';

@Component({
  selector: 'app-student-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, PageHeaderComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './student-form.html',
})
export class StudentFormComponent {
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private studentService = inject(StudentService);
  private classService = inject(ClassService);

  classes = signal<SchoolClass[]>([]);
  studentId = this.route.snapshot.paramMap.get('id');
  isEdit = !!this.studentId;
  saved = signal(false);

  form = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    gender: ['Male', Validators.required],
    dob: ['', Validators.required],
    bloodGroup: [''],
    classId: ['', Validators.required],
    sectionId: ['', Validators.required],
    rollNo: ['', Validators.required],
    admissionDate: [new Date().toISOString().slice(0, 10), Validators.required],
    guardianName: ['', Validators.required],
    guardianRelation: ['Father', Validators.required],
    guardianPhone: ['', [Validators.required, Validators.pattern(/^[0-9+\s-]{7,15}$/)]],
    guardianEmail: ['', [Validators.required, Validators.email]],
    guardianOccupation: [''],
    addressLine: ['', Validators.required],
    city: ['', Validators.required],
    state: ['', Validators.required],
    pincode: ['', Validators.required],
    notes: [''],
  });

  constructor() {
    this.classService.getAll().subscribe(list => this.classes.set(list));
    if (this.studentId) {
      this.studentService.getById(this.studentId).subscribe(s => {
        if (!s) return;
        this.form.patchValue({
          firstName: s.firstName, lastName: s.lastName, gender: s.gender, dob: s.dob,
          bloodGroup: s.bloodGroup, classId: s.classId, sectionId: s.sectionId, rollNo: s.rollNo,
          admissionDate: s.admissionDate, guardianName: s.guardian.name, guardianRelation: s.guardian.relation,
          guardianPhone: s.guardian.phone, guardianEmail: s.guardian.email, guardianOccupation: s.guardian.occupation,
          addressLine: s.address.line1, city: s.address.city, state: s.address.state, pincode: s.address.pincode,
        });
      });
    }
  }

  availableSections = computed(() => {
    const cls = this.classes().find(c => c.id === this.form.controls.classId.value);
    return cls ? cls.sections : [];
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saved.set(true);
    setTimeout(() => this.router.navigate(['/students']), 900);
  }
}
