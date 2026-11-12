"""
End-to-end check of the admin password reset (POST /api/PasswordReset) against a SCRATCH copy of the database. Never run it against a real database:
it registers a throwaway school and seeds throwaway logins with psql.

What it proves (the parts a unit test with a fake repository cannot):
  * the old password stops working, the new temporary password works and arrives flagged is_first_login
  * the user's old refresh token stops working, and an old access token is held by the first-login rule (403) until the password is changed
  * one audit row (who / target / when / reason) and no password in it
  * Super Admin -> School Admin works, Super Admin -> teacher is 403, School Admin -> another school's login is 404, self is 403, a Teacher is 403, an inactive login is 409
  * the temporary password never appears in the API log (pass --log)

Usage (scratch API already running against the scratch database):
  PGPASSWORD=... SEED_HASH='<bcrypt of SEED_PASSWORD>' python scripts/password-reset-smoke.py --api http://127.0.0.1:5291 --db smss_scratch_pr \
      --super-login SUAD001 --super-password '<its password>' --seed-password '<the password SEED_HASH is for>' --log path/to/api.log
Needs psql on PATH. Exit code 0 = everything held.
"""
import argparse, json, os, subprocess, sys, urllib.request, urllib.error

p = argparse.ArgumentParser()
p.add_argument('--api', required=True)
p.add_argument('--db', required=True)
p.add_argument('--super-login', required=True)
p.add_argument('--super-password', required=True)
p.add_argument('--seed-password', required=True)
p.add_argument('--log')
p.add_argument('--code', default='95' + str(os.getpid() % 100000).zfill(5) + '1')
a = p.parse_args()
SEED_HASH = os.environ['SEED_HASH']
failures = []


def check(name, cond, detail=''):
    print(('PASS ' if cond else 'FAIL ') + name + (f'  [{detail}]' if detail and not cond else ''))
    if not cond: failures.append(name)


def call(method, path, body=None, token=None):
    h = {'Content-Type': 'application/json'}
    if token: h['Authorization'] = 'Bearer ' + token
    r = urllib.request.Request(a.api + '/api' + path, data=None if body is None else json.dumps(body).encode(), method=method, headers=h)
    try:
        with urllib.request.urlopen(r) as x: return x.status, json.loads(x.read() or b'{}')
    except urllib.error.HTTPError as e:
        raw = e.read()
        return e.code, (json.loads(raw) if raw else {})


def sql(q):
    out = subprocess.run(['psql', '-h', 'localhost', '-U', 'postgres', '-d', a.db, '-At', '-v', 'ON_ERROR_STOP=1', '-c', q], capture_output=True, text=True)
    if out.returncode != 0: raise SystemExit('psql failed: ' + out.stderr.strip())
    return out.stdout.strip()


def login(login_id, password):
    s, r = call('POST', '/Auth/login', {'loginId': login_id, 'password': password})
    return s, (r.get('data') or {}), r


def seed_user(login_id, school, role, status='Active'):
    sql(f"""insert into tb_users (school_id, user_type, org_user_id, display_name, password_hash, is_first_login, status_id, created_at, updated_at)
            select '{school}', 'Employee', '{login_id}', '{login_id} name', '{SEED_HASH}', false, sid, now(), now() from lut_status where sname='{status}' and stype='general status';
            insert into tb_user_roles (user_id, role_id, created_at) select u.user_id, r.role_id, now() from tb_users u, lut_roles r where u.org_user_id='{login_id}' and r.role_name='{role}';""")


# ---- set-up: a throwaway school (registered through the API) and throwaway logins --------------------------------------------------------------
s, sup, _ = login(a.super_login, a.super_password)
check('super admin signs in', s == 200)
tok = sup['token']
s, r = call('POST', '/SchoolRegistration/register', {
    'schoolCode': a.code, 'schoolName': 'Reset Smoke School', 'schoolShortName': 'RSMK', 'email': 'throwaway@example.invalid', 'mobileNumber': '9876543210',
    'contacts': [{'contactType': 'Principal', 'contactName': 'T One', 'email': 't1@example.invalid', 'mobileNumber': '9876543211', 'isPrimary': True}]}, tok)
check('throwaway school registered', s == 201, r.get('message'))
school = r['data']['school']['schoolId']; admin_login = r['data']['loginId']; admin_temp = r['data']['temporaryPassword']
other = sql("select school_id from tb_schools where school_id <> '%s' and school_id not in (select school_id from tb_users where user_type='Super Admin' and school_id is not null) limit 1" % school)
seed_user('RSMKTCH1', school, 'Teacher'); seed_user('RSMKSTU1', school, 'Student'); seed_user('RSMKOFF1', school, 'Teacher', status='Inactive'); seed_user('RSMKOTH1', other, 'Teacher')

s, d, _ = login(admin_login, admin_temp)
check('new school admin signs in on the temporary password', s == 200 and d['user']['isFirstLogin'] is True)
s, r = call('POST', '/Auth/change-password', {'currentPassword': admin_temp, 'newPassword': 'Admin#Smoke1x', 'confirmPassword': 'Admin#Smoke1x'}, d['token'])
check('school admin changes the temporary password', s == 200, r.get('message'))
s, d, _ = login(admin_login, 'Admin#Smoke1x'); admin_tok = d['token']
check('school admin signs in normally', s == 200 and d['user']['isFirstLogin'] is False)

# ---- the teacher has a live session (access + refresh token) --------------------------------------------------------------------------------------
s, t, _ = login('RSMKTCH1', a.seed_password)
check('teacher signs in', s == 200)
old_access, old_refresh = t['token'], t['refreshToken']
s, _r = call('GET', '/Notice/my', None, old_access)
check('teacher\'s old access token works before the reset', s == 200, str(s))

# ---- the reset -----------------------------------------------------------------------------------------------------------------------------------------
s, r = call('POST', '/PasswordReset', {'loginId': 'RSMKTCH1', 'reason': 'Forgot it after the holidays'}, admin_tok)
check('school admin resets the teacher', s == 200, r.get('message'))
temp = (r.get('data') or {}).get('temporaryPassword', '')
check('a 12 character temporary password is returned once', len(temp) == 12)

s, _d, _r = login('RSMKTCH1', a.seed_password)
check('the OLD password stops working', s == 401, str(s))
s, d, _r = login('RSMKTCH1', temp)
check('the new temporary password works and is flagged first login', s == 200 and d['user']['isFirstLogin'] is True, str(s))
new_access = d['token']
s, _r = call('POST', '/Auth/refresh', {'refreshToken': old_refresh})
check('the OLD refresh token stops working', s == 401, str(s))
s, r = call('GET', '/Notice/my', None, old_access)
check('the OLD access token is held by the first-login rule (403)', s == 403 and 'Password change' in r.get('message', ''), f'{s} {r.get("message")}')
s, r = call('GET', '/Notice/my', None, new_access)
check('the new session is held too until the password is changed', s == 403, str(s))
s, r = call('POST', '/Auth/change-password', {'currentPassword': temp, 'newPassword': 'Teacher#New1x', 'confirmPassword': 'Teacher#New1x'}, new_access)
check('the teacher changes the temporary password', s == 200, r.get('message'))
s, _r = call('GET', '/Notice/my', None, r['data']['token'])
check('after the change the teacher is released', s == 200, str(s))

# ---- audit ---------------------------------------------------------------------------------------------------------------------------------------------
row = sql("select count(*), min(reason), min(target_login_id), min(school_id), bool_and(reset_at > now() - interval '5 minutes'), min(reset_by_user_id::text) from tb_password_reset_audit where target_login_id='RSMKTCH1'")
check('exactly one audit row with the reason, target, school and time', row.startswith('1|Forgot it after the holidays|RSMKTCH1|' + school + '|t|'), row)
check('the audit row was written by the school admin', sql(f"select count(*) from tb_password_reset_audit a join tb_users u on u.user_id=a.reset_by_user_id where u.org_user_id='{admin_login}' and a.target_login_id='RSMKTCH1'") == '1')
check('the audit table has no password column', sql("select count(*) from information_schema.columns where table_name='tb_password_reset_audit' and column_name ilike '%pass%'") == '0')
check('the temporary password is not stored anywhere in the audit row', temp not in sql("select string_agg(a::text, ' ') from tb_password_reset_audit a"))

# ---- who may reset whom (end to end, with real tokens) ---------------------------------------------------------------------------------------------
def reset(token, login_id, reason='test reason'): return call('POST', '/PasswordReset', {'loginId': login_id, 'reason': reason}, token)[0]
check('school admin -> a student of the school: 200', reset(admin_tok, 'RSMKSTU1') == 200)
check('school admin -> another school\'s teacher: 404', reset(admin_tok, 'RSMKOTH1') == 404)
check('school admin -> themselves: 403', reset(admin_tok, admin_login) == 403)
check('school admin -> an inactive login: 409', reset(admin_tok, 'RSMKOFF1') == 409)
check('school admin -> a missing login: 404', reset(admin_tok, 'NOSUCHLOGIN') == 404)
check('school admin -> a blank reason: 400', reset(admin_tok, 'RSMKTCH1', '   ') == 400)
s, t2, _ = login('RSMKTCH1', 'Teacher#New1x')
check('a teacher cannot reset anybody', reset(t2['token'], 'RSMKSTU1') == 403)
check('super admin -> the school admin: 200', reset(tok, admin_login) == 200)
check('super admin -> a teacher: 403', reset(tok, 'RSMKTCH1') == 403)
check('super admin -> themselves: 403', reset(tok, a.super_login) == 403)
check('no token: 401', call('POST', '/PasswordReset', {'loginId': 'RSMKTCH1', 'reason': 'x'})[0] == 401)

if a.log and os.path.exists(a.log):
    log = open(a.log, encoding='utf-8', errors='replace').read()
    check('the temporary password never appears in the API log', temp not in log and 'Forgot it after the holidays' not in log)

print('\n%d failed' % len(failures) if failures else '\nall checks passed')
sys.exit(1 if failures else 0)
