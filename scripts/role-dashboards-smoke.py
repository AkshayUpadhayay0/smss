"""
Checks the Principal, Accountant and Admission Staff dashboards against raw SQL, and the authorization matrix over HTTP, on a SCRATCH copy of the database (never a real one:
it seeds throwaway logins with psql). For each school given it
  * seeds a Principal, Accountant, Admission Staff, Teacher, Student and Guardian login (throwaway ids PRIN... ACCT... ADMS... TCHR... STUD... GUAR..., all with SEED_HASH),
  * calls the three endpoints with every role's token (200 for the one right role, 403 for all others) and with no token (401),
  * recomputes every figure of each dashboard with its own SQL (not the API's code) and compares it with the response.

Usage (scratch API already running against the scratch database):
  PGPASSWORD=... SEED_HASH='<bcrypt of SEED_PASSWORD>' python scripts/role-dashboards-smoke.py --api http://127.0.0.1:5293 --db smss_scratch_dash \
      --seed-password '<password>' --schools LAS261103001 GHS261103002 --super-login SUAD001
Needs psql on PATH. Exit code 0 = everything held.
"""
import argparse, json, os, subprocess, sys, urllib.request, urllib.error

p = argparse.ArgumentParser()
p.add_argument('--api', required=True)
p.add_argument('--db', required=True)
p.add_argument('--seed-password', required=True)
p.add_argument('--schools', nargs='+', required=True)
p.add_argument('--super-login', required=True)
p.add_argument('--tag', default='ZZ')
a = p.parse_args()
SEED_HASH = os.environ['SEED_HASH']
failures = []


def check(name, cond, detail=''):
    print(('PASS ' if cond else 'FAIL ') + name + (f'  [{detail}]' if detail and not cond else ''))
    if not cond: failures.append(name)


def call(path, token=None):
    h = {'Content-Type': 'application/json'}
    if token: h['Authorization'] = 'Bearer ' + token
    r = urllib.request.Request(a.api + '/api' + path, headers=h)
    try:
        with urllib.request.urlopen(r) as x: return x.status, json.loads(x.read() or b'{}')
    except urllib.error.HTTPError as e:
        raw = e.read()
        return e.code, (json.loads(raw) if raw else {})


def login(login_id):
    r = urllib.request.Request(a.api + '/api/Auth/login', data=json.dumps({'loginId': login_id, 'password': a.seed_password}).encode(), method='POST', headers={'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(r) as x: return json.loads(x.read())['data']['token']
    except urllib.error.HTTPError as e:
        raise SystemExit(f'login {login_id} failed: {e.code} {e.read()[:200]}')


def sql(q):
    out = subprocess.run(['psql', '-h', 'localhost', '-U', 'postgres', '-d', a.db, '-At', '-F', '|', '-v', 'ON_ERROR_STOP=1', '-c', q], capture_output=True, text=True)
    if out.returncode != 0: raise SystemExit('psql failed: ' + out.stderr.strip())
    return out.stdout.strip()


def rows(q): return [r.split('|') for r in sql(q).splitlines() if r != '']
def one(q):
    r = rows(q)
    return r[0] if r else []
def num(x): return float(x or 0)


ROLES = {'PRIN': 'Principal', 'ACCT': 'Accountant', 'ADMS': 'Admission Staff', 'TCHR': 'Teacher', 'STUD': 'Student', 'GUAR': 'Guardian'}
ENDPOINTS = {'/PrincipalDashboard/summary': 'Principal', '/AccountantDashboard/summary': 'Accountant', '/AdmissionDashboard/summary': 'Admission Staff'}


def seed(school):
    ids = {}
    for pre, role in ROLES.items():
        lid = f'{a.tag}{pre}{school[:6]}'
        ids[role] = lid
        sql(f"""delete from tb_user_roles where user_id in (select user_id from tb_users where org_user_id='{lid}');
                delete from tb_users where org_user_id='{lid}';
                insert into tb_users (school_id, user_type, org_user_id, display_name, password_hash, is_first_login, status_id, created_at, updated_at)
                  select '{school}', 'Employee', '{lid}', '{lid} name', '{SEED_HASH}', false, sid, now(), now() from lut_status where sname='Active' and stype='general status';
                insert into tb_user_roles (user_id, role_id, created_at) select u.user_id, r.role_id, now() from tb_users u, lut_roles r where u.org_user_id='{lid}' and r.role_name='{role}';""")
    return ids


A = sql("select sid from lut_status where sname='Active' and stype='general status'")
T = sql("select (now() at time zone 'Asia/Kolkata')::date")
super_tok = login(a.super_login)

for school in a.schools:
    print(f'\n===== school {school}  (India date {T}) =====')
    ids = seed(school)
    toks = {role: login(lid) for role, lid in ids.items()}
    sa = sql(f"select org_user_id from tb_users where org_user_id='{school}'")
    if sa:
        sql(f"update tb_users set password_hash='{SEED_HASH}', is_first_login=false where org_user_id='{school}'")
        toks['School Admin'] = login(school)
    toks['Super Admin'] = super_tok

    # ---------------------------------------------------------------- authorization matrix
    for path, allowed in ENDPOINTS.items():
        for role, tok in toks.items():
            s, _ = call(path, tok)
            check(f'{path}: {role} -> {"200" if role == allowed else "403"}', s == (200 if role == allowed else 403), str(s))
        s, _ = call(path)
        check(f'{path}: anonymous -> 401', s == 401, str(s))

    Y = one(f"select academic_year_id, year_name from tb_academic_years where school_id='{school}' and start_date<='{T}' and end_date>='{T}' order by is_current desc, start_date desc limit 1")

    # ---------------------------------------------------------------- Principal
    s, r = call('/PrincipalDashboard/summary', toks['Principal'])
    d = r['data']; st = d['stats']
    check('principal: no part unavailable on a healthy database', d['partsUnavailable'] == [], str(d['partsUnavailable']))
    students = int(sql(f"""select count(*) from tb_student_enrollments e join tb_students s on s.student_id=e.student_id join tb_sections sec on sec.section_id=e.class_section_id join tb_classes c on c.class_id=sec.class_id
        where e.school_id='{school}' and e.academic_year_id={Y[0]} and e.status_id={A} and s.status_id={A} and sec.school_id='{school}' and c.school_id='{school}' and sec.status_id={A} and c.status_id={A}"""))
    check('principal: students', st['totalStudents'] == students, f"{st['totalStudents']} vs {students}")
    staff = int(sql(f"select count(*) from tb_employees where school_id='{school}' and status_id={A}"))
    check('principal: staff', st['totalStaff'] == staff, f"{st['totalStaff']} vs {staff}")
    att = rows(f"""select a.attendance_date, count(*), count(*) filter (where l.status_code='PRESENT') from tb_student_attendance a join lut_attendance_status l on l.attendance_status_id=a.attendance_status_id
        where a.school_id='{school}' and a.attendance_date between '{T}'::date-13 and '{T}' group by 1""")
    byday = {r_[0]: (int(r_[1]), int(r_[2])) for r_ in att}
    td = byday.get(T, (0, 0))
    check('principal: attendance marked/present today', (st['attendanceMarkedToday'], st['attendancePresentToday']) == td, f"{(st['attendanceMarkedToday'], st['attendancePresentToday'])} vs {td}")
    expect_pct = None if td[0] == 0 else round(td[1] * 100 / td[0] + 1e-9, 1)
    check('principal: attendance % today', st['attendancePercentToday'] == expect_pct, f"{st['attendancePercentToday']} vs {expect_pct}")
    ok = len(d['attendanceTrend']) == 14
    for pt in d['attendanceTrend']:
        m, pr = byday.get(pt['date'][:10], (0, 0))
        ok = ok and (pt['marked'], pt['present']) == (m, pr) and pt['presentPercent'] == (None if m == 0 else round(pr * 100 / m + 1e-9, 1))
    check('principal: 14-day trend, every day', ok)
    fee = one(f"""select coalesce(sum(d.amount_due),0), coalesce(sum(d.amount_paid),0) from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id
        where d.school_id='{school}' and i.school_id='{school}' and i.academic_year_id={Y[0]}""")
    check('principal: fee due / collected / outstanding', (num(st['feeTotalDue']), num(st['feeCollected']), num(st['feeOutstanding'])) == (num(fee[0]), num(fee[1]), max(num(fee[0]) - num(fee[1]), 0)), f"{st['feeTotalDue']},{st['feeCollected']},{st['feeOutstanding']} vs {fee}")
    leave = int(sql(f"select count(*) from tb_employee_leave_requests where school_id='{school}' and status='Pending'"))
    check('principal: pending employee leave', d['alerts']['pendingEmployeeLeaves'] == leave, f"{d['alerts']['pendingEmployeeLeaves']} vs {leave}")
    for key, name in (('admissionsToDecide', 'UNDER_REVIEW'), ('admissionsAwaitingReview', 'APPLICATION_SUBMITTED')):
        n = int(sql(f"select count(*) from tb_admissions a join lut_status l on l.sid=a.status_id where a.school_id='{school}' and l.stype='ADMISSION_STATUS' and l.sname='{name}'"))
        check(f'principal: {key}', d['alerts'][key] == n, f"{d['alerts'][key]} vs {n}")
    pub = [int(x[0]) for x in rows(f"select publication_id from tb_report_publications where school_id='{school}' order by published_at desc, publication_id desc limit 5")]
    check('principal: latest published results (ids, newest first)', [x['publicationId'] for x in d['recentPublications']] == pub, f"{[x['publicationId'] for x in d['recentPublications']]} vs {pub}")
    notices = [int(x[0]) for x in rows(f"select notice_id from tb_notices where school_id='{school}' and status_id={A} order by published_at desc, notice_id desc limit 5")]
    check('principal: latest notices (ids, newest first)', [x['noticeId'] for x in d['recentNotices']] == notices, f"{[x['noticeId'] for x in d['recentNotices']]} vs {notices}")
    ev_total = int(sql(f"select count(*) from tb_school_events where school_id='{school}' and status_id={A} and end_date>='{T}'"))
    check('principal: upcoming events (count, at most 5)', len(d['upcomingEvents']) == min(5, ev_total), f"{len(d['upcomingEvents'])} vs {min(5, ev_total)}")

    # ---------------------------------------------------------------- Accountant
    s, r = call('/AccountantDashboard/summary', toks['Accountant'])
    d = r['data']; st = d['stats']
    check('accountant: no part unavailable on a healthy database', d['partsUnavailable'] == [], str(d['partsUnavailable']))
    today_amt = num(sql(f"select coalesce(sum(amount),0) from tb_fee_payments where school_id='{school}' and status='Active' and (payment_date at time zone 'Asia/Kolkata')::date='{T}'"))
    check('accountant: collected today', num(st['collectedToday']) == today_amt, f"{st['collectedToday']} vs {today_amt}")
    spans = [int(x[0]) for x in rows(f"select academic_year_id from tb_academic_years where school_id='{school}' and start_date <= (date_trunc('month','{T}'::date)+interval '1 month - 1 day')::date and end_date >= (date_trunc('month','{T}'::date)-interval '5 months')::date")]
    ys = ','.join(map(str, spans)) or '0'

    def month_amount(offset):
        return num(sql(f"""select coalesce(sum(al.allocated_amount),0) from tb_fee_payment_allocations al join tb_fee_payments p on p.payment_id=al.payment_id
            join tb_student_fee_dues d on d.fee_due_id=al.fee_due_id join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id
            where p.school_id='{school}' and p.status='Active' and i.academic_year_id in ({ys})
              and date_trunc('month', p.payment_date at time zone 'Asia/Kolkata') = date_trunc('month','{T}'::date) - interval '{offset} months'"""))
    trend = [month_amount(5 - i) for i in range(6)]
    check('accountant: 6-month chart, every month', [num(m['amount']) for m in d['collectionTrend']] == trend, f"{[m['amount'] for m in d['collectionTrend']]} vs {trend}")
    check('accountant: collected this month equals the chart and the SQL', num(st['collectedThisMonth']) == trend[-1], f"{st['collectedThisMonth']} vs {trend[-1]}")
    dues = one(f"""select coalesce(sum(d.amount_due-d.amount_paid) filter (where true),0),
        coalesce(sum(d.amount_due-d.amount_paid) filter (where d.amount_paid<d.amount_due and d.due_date<'{T}'),0),
        count(*) filter (where d.amount_paid<d.amount_due and d.due_date<'{T}'),
        coalesce(sum(d.amount_due-d.amount_paid) filter (where d.amount_paid<d.amount_due and d.due_date>='{T}' and d.due_date<='{T}'::date+7),0),
        count(*) filter (where d.amount_paid<d.amount_due and d.due_date>='{T}' and d.due_date<='{T}'::date+7),
        coalesce(sum(d.amount_due),0), coalesce(sum(d.amount_paid),0)
        from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where d.school_id='{school}' and i.school_id='{school}' and i.academic_year_id={Y[0]}""")
    check('accountant: outstanding', num(st['outstanding']) == max(num(dues[5]) - num(dues[6]), 0), f"{st['outstanding']} vs {max(num(dues[5]) - num(dues[6]), 0)}")
    check('accountant: overdue amount and count', (num(st['overdueAmount']), st['overdueCount']) == (num(dues[1]), int(dues[2])), f"{(st['overdueAmount'], st['overdueCount'])} vs {(dues[1], dues[2])}")
    check('accountant: upcoming 7 days amount and count', (num(st['upcomingAmount']), st['upcomingCount']) == (num(dues[3]), int(dues[4])), f"{(st['upcomingAmount'], st['upcomingCount'])} vs {(dues[3], dues[4])}")
    pays = [int(x[0]) for x in rows(f"select payment_id from tb_fee_payments where school_id='{school}' and status='Active' order by payment_date desc, payment_id desc limit 10")]
    check('accountant: last 10 payments (ids, newest first)', [x['paymentId'] for x in d['recentPayments']] == pays, f"{[x['paymentId'] for x in d['recentPayments']]} vs {pays}")
    amounts = {int(x[0]): num(x[1]) for x in rows(f"select payment_id, amount from tb_fee_payments where school_id='{school}' and status='Active'")}
    check('accountant: each payment amount', all(num(x['amount']) == amounts[x['paymentId']] for x in d['recentPayments']))

    # ---------------------------------------------------------------- Admission Staff
    s, r = call('/AdmissionDashboard/summary', toks['Admission Staff'])
    d = r['data']
    check('admission: no part unavailable on a healthy database', d['partsUnavailable'] == [], str(d['partsUnavailable']))
    counts = {x[0]: int(x[1]) for x in rows(f"select l.sname, count(*) from tb_admissions a join lut_status l on l.sid=a.status_id where a.school_id='{school}' and l.stype='ADMISSION_STATUS' group by 1")}
    order = ['APPLICATION_SUBMITTED', 'UNDER_REVIEW', 'APPROVED', 'ADMITTED', 'REJECTED', 'CANCELLED']
    check('admission: applications by status (all six, in order)', [(x['statusName'], x['count']) for x in d['byStatus']] == [(s_, counts.get(s_, 0)) for s_ in order], str(d['byStatus']))
    total = int(sql(f"select count(*) from tb_admissions where school_id='{school}'"))
    check('admission: total', d['totalApplications'] == total, f"{d['totalApplications']} vs {total}")
    newk = int(sql(f"select count(*) from tb_admissions where school_id='{school}' and application_date >= '{T}'::date-6 and application_date <= '{T}'"))
    check('admission: new this week', d['newThisWeek'] == newk, f"{d['newThisWeek']} vs {newk}")
    missing = rows(f"""select a.admission_id, array_to_string(array(select t.document_name from tb_document_types t where t.school_id=a.school_id and t.status_id={A} and t.is_required and t.applies_to in ('STUDENT','BOTH')
            and not exists (select 1 from tb_admission_application_documents d where d.admission_id=a.admission_id and d.document_type_id=t.document_type_id and d.status_id={A}) order by t.document_type_id), ';') as miss
        from tb_admissions a join lut_status l on l.sid=a.status_id where a.school_id='{school}' and l.stype='ADMISSION_STATUS' and l.sname in ('APPLICATION_SUBMITTED','UNDER_REVIEW','APPROVED')
        order by a.application_date desc, a.admission_id desc""")
    missing = [(int(m[0]), [x for x in m[1].split(';') if x]) for m in missing if m[1]]
    check('admission: applications missing documents (count)', d['missingDocumentsCount'] == len(missing), f"{d['missingDocumentsCount']} vs {len(missing)}")
    check('admission: missing documents list (ids, newest first, at most 10)', [m['admissionId'] for m in d['missingDocuments']] == [m[0] for m in missing[:10]])
    check('admission: which documents are missing', all(sorted(x['missingDocuments']) == sorted(dict(missing)[x['admissionId']]) for x in d['missingDocuments']))
    recent = [int(x[0]) for x in rows(f"select admission_id from tb_admissions where school_id='{school}' order by application_date desc, admission_id desc limit 10")]
    check('admission: last 10 applications (ids, newest first)', [x['admissionId'] for x in d['recentApplications']] == recent, f"{[x['admissionId'] for x in d['recentApplications']]} vs {recent}")

    print(f"   (for reference: students={students}, payments on file={len(amounts)}, applications={total}, open applications missing documents={len(missing)})")

print('\n%d failed' % len(failures) if failures else '\nall checks passed')
sys.exit(1 if failures else 0)
