"""Creates a throwaway Teacher and Student in the ZZ school (through the API) for the Student / Teacher regression check. Passwords are random and kept in session.json only."""
import json, secrets
import api

st = api.session()
pw = lambda: 'Zz' + secrets.token_urlsafe(12) + '#9'
tok, _ = api.login(st['admin']['loginId'], st['admin']['password'])
sid = st['schoolId']

# ---- Teacher: employee + login with the Teacher role
emp = api.ok(api.call('POST', '/api/Employee', tok, {'designationId': st['designationId'], 'departmentId': st['departmentId'], 'employeeType': 'Teaching', 'firstName': 'ZZ', 'lastName': 'Teacher',
        'dateOfBirth': '1985-02-02', 'mobileNumber': '9000000041', 'email': 'zz-teacher@example.invalid', 'address': 'ZZ Staff Quarters', 'dateOfJoining': '2021-04-01'}), 'teacher employee')
lk = api.ok(api.call('GET', '/api/Employee/lookups', tok), 'lookups')
role = next(x['roleId'] for x in lk['assignableRoles'] if x['roleName'] == 'Teacher')
lg = api.ok(api.call('POST', f"/api/Employee/{emp['employeeId']}/create-login", tok, {'roleIds': [role]}), 'teacher login')
t2, _ = api.login(lg['orgUserId'], lg['temporaryPassword'])
tpw = pw()
api.ok(api.call('POST', '/api/Auth/change-password', t2, {'currentPassword': lg['temporaryPassword'], 'newPassword': tpw}), 'teacher pw', (200,))
st['teacher'] = {'loginId': lg['orgUserId'], 'password': tpw, 'employeeId': emp['employeeId']}
api.save(st)
print('teacher', lg['orgUserId'])

# ---- Student: academic year, class, section, admission type, then the direct entry and a login
ay = api.ok(api.call('POST', '/api/AcademicYear', tok, {'yearName': '2026-27', 'startDate': '2026-04-01', 'endDate': '2027-03-31'}), 'year')
api.ok(api.call('POST', f"/api/AcademicYear/{ay['academicYearId']}/set-current", tok), 'set current', (200,))
cl = api.ok(api.call('POST', '/api/Classes', tok, {'className': 'ZZ Class 1', 'classCode': 'ZZC1', 'sequenceOrder': 1}), 'class')
sec = api.ok(api.call('POST', '/api/Sections', tok, {'classId': cl['classId'], 'sectionName': 'A', 'maxStrength': 40}), 'section')
# admission types are a GLOBAL master now: read the active ones (the Super Admin must have added "Existing Student")
adm = next((t for t in api.ok(api.call('GET', '/api/AdmissionTypes', tok), 'admission types') if t['typeName'].lower() == 'existing student'), None)
assert adm, 'the "Existing Student" admission type is missing: the Super Admin must add it under Masters > Admission Type'
st['rolecheck'] = {'academicYearId': ay['academicYearId'], 'classId': cl['classId'], 'sectionId': sec['sectionId'], 'admissionTypeId': adm['admissionTypeId']}
api.save(st)
body = {'admissionDate': '2026-04-05T00:00:00', 'academicYearId': ay['academicYearId'], 'classSectionId': sec['sectionId'], 'rollNumber': '1', 'firstName': 'ZZ', 'lastName': 'Pupil',
        'dateOfBirth': '2018-05-05T00:00:00', 'genderId': 1, 'bloodGroupId': 1, 'studentCategoryId': 3, 'guardianName': 'ZZ Guardian', 'guardianRelationship': 'FATHER', 'guardianMobileNumber': '9000000051'}
r = api.call('POST', f'/api/StudentOnboarding/{sid}/existing-student', tok, body)
print('onboard', r[0], json.dumps(r[1])[:400])
stu = api.ok(r, 'student')
lg = api.ok(api.call('POST', f"/api/Student/{sid}/{stu['studentId']}/create-login", tok), 'student login')
t3, _ = api.login(lg['orgUserId'], lg['temporaryPassword'])
spw = pw()
api.ok(api.call('POST', '/api/Auth/change-password', t3, {'currentPassword': lg['temporaryPassword'], 'newPassword': spw}), 'student pw', (200,))
st['student'] = {'loginId': lg['orgUserId'], 'password': spw, 'studentId': stu['studentId']}
api.save(st)
print('student', lg['orgUserId'])
