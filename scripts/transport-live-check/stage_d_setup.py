"""Seeds ONE throwaway student in the ZZ school of smss-db-dev (one SQL row: the dev DB predates the global admission-type migration, so the API's
onboarding cannot run there), then creates the Student LOGIN through the API with a random password kept only in session.json."""
import secrets
import api, devdb

st = api.session()
sid = st['schoolId']
q = devdb.psql
active = q("select sid from lut_status where sname='Active' and stype='general status'").strip()
year = q(f"select academic_year_id from tb_academic_years where school_id='{sid}' and is_current limit 1").strip() or q(f"select academic_year_id from tb_academic_years where school_id='{sid}' limit 1").strip()
print('active sid', active, 'year', year)
student_id = 'ZZDE001ST001'
if q(f"select count(*) from tb_students where school_id='{sid}' and student_id='{student_id}'").strip() == '0':
    q(f"insert into tb_students (student_id, school_id, admission_number, first_name, last_name, date_of_birth, admission_type_id, admission_date, status_id) "
      f"values ('{student_id}', '{sid}', 'ZZ-ADM-1', 'ZZ', 'Pupil', '2018-05-05', 1, '2026-04-05', {active})")
tok, _ = api.login(st['admin']['loginId'], st['admin']['password'])
lg = api.ok(api.call('POST', f'/api/Student/{sid}/{student_id}/create-login', tok), 'student login')
t3, _ = api.login(lg['orgUserId'], lg['temporaryPassword'])
pw = 'Zz' + secrets.token_urlsafe(12) + '#9'
api.ok(api.call('POST', '/api/Auth/change-password', t3, {'currentPassword': lg['temporaryPassword'], 'newPassword': pw}), 'student pw', (200,))
st['student'] = {'loginId': lg['orgUserId'], 'password': pw, 'studentId': student_id}
st['rolecheck'] = {'academicYearId': int(year)}
api.save(st)
print('student', lg['orgUserId'])
