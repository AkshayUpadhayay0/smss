import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { Admission, AdmissionStatus } from '../../models/admission.model';
import { AdmissionActionsComponent } from './admission-actions.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const app = (statusName: AdmissionStatus): Admission => ({
  admissionId: 7, applicationNumber: 'ADM20260007', applicantFullName: 'Anil Applicant', statusName, applicationDate: '2026-10-20', academicYearId: 6, academicYearName: '2026-27',
  appliedClassId: 3, appliedClassName: 'Class 1', admissionTypeId: 1, admissionTypeName: 'New Admission',
});

describe('AdmissionActionsComponent: who sees which action', () => {
  let fixture: ComponentFixture<AdmissionActionsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const labels = () => [...el().querySelectorAll('app-button button')].map((b) => b.textContent?.trim().replace(/\s+/g, ' '));
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(role: string, status: AdmissionStatus) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AdmissionActionsComponent);
    fixture.componentRef.setInput('admission', app(status));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it.each(['School Admin', 'Principal'])('%s: an Under Review application offers Approve, Reject and Withdraw', (role) => {
    open(role, 'UNDER_REVIEW');
    expect(labels()).toEqual(['Approve', 'Reject', 'Withdraw']);
  });

  it('Admission Staff: an Under Review application offers ONLY Withdraw - no Approve, no Reject', () => {
    open('Admission Staff', 'UNDER_REVIEW');
    expect(labels()).toEqual(['Withdraw']);
  });

  it('every one of the three can start a review of a Submitted application (and withdraw it)', () => {
    for (const role of ['School Admin', 'Principal', 'Admission Staff']) {
      TestBed.resetTestingModule();
      open(role, 'APPLICATION_SUBMITTED');
      expect(labels()).toEqual(['Start Review', 'Withdraw']);
      http.verify();
    }
  });

  it('an Approved application offers Confirm Admission to all three; a decided or closed one offers nothing', () => {
    open('Admission Staff', 'APPROVED');
    expect(labels()).toEqual(['Confirm Admission']);
    for (const status of ['REJECTED', 'ADMITTED', 'CANCELLED'] as AdmissionStatus[]) {
      TestBed.resetTestingModule();
      open('School Admin', status);
      expect(labels()).toEqual([]);
      http.verify();
    }
  });

  it('Start Review asks for optional notes and sends them', () => {
    open('Admission Staff', 'APPLICATION_SUBMITTED');
    cmp()['run']('start-review');
    fixture.detectChanges();
    expect(document.body.textContent).toContain('Review notes');
    cmp()['remarks'].setValue('  Documents complete  ');
    cmp()['submitDialog']();
    const post = http.expectOne(`${API}/api/Admission/S/7/start-review`);
    expect(post.request.body).toEqual({ notes: 'Documents complete' });
    post.flush(env({ ...app('UNDER_REVIEW'), reviewNotes: 'Documents complete' }));
    expect(cmp()['dialog']()).toBeNull();
  });

  it('Reject needs a reason: an empty one is not sent', () => {
    open('Principal', 'UNDER_REVIEW');
    cmp()['run']('reject');
    cmp()['submitDialog']();
    http.expectNone(`${API}/api/Admission/S/7/reject`);
    expect(cmp()['remarksError']()).toContain('reason is required');
    cmp()['remarks'].setValue('Over the age limit');
    cmp()['submitDialog']();
    const post = http.expectOne(`${API}/api/Admission/S/7/reject`);
    expect(post.request.body).toEqual({ remarks: 'Over the age limit' });
    post.flush(env(app('REJECTED')));
  });

  it('Withdraw posts to cancel with the remarks', () => {
    open('Admission Staff', 'UNDER_REVIEW');
    cmp()['run']('cancel');
    cmp()['remarks'].setValue('Family moved');
    cmp()['submitDialog']();
    const post = http.expectOne(`${API}/api/Admission/S/7/cancel`);
    expect(post.request.body).toEqual({ remarks: 'Family moved' });
    post.flush(env(app('CANCELLED')));
  });
});
