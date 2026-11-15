"""Stage D live check (approximate pickup / drop times) against the scratch API and smss-db-dev ONLY.
Needs, in this order: startapi.py (default thresholds are fine), stage_a.py, rolecheck_setup.py (one throwaway student).
Everything it creates is ZZ data inside the throwaway school; clean up with cleanup.py afterwards."""
import json, urllib.error, urllib.request
import api, devdb

st = api.session()
rows = []
sql = devdb.psql


def chk(label, cond, detail=''):
    rows.append((label, bool(cond), detail))
    print(('PASS ' if cond else 'FAIL ') + label + (f'  [{detail}]' if detail and not cond else ''))


def raw(method, path, token=None, body=None, duty=None):
    h = {'Content-Type': 'application/json'}
    if token: h['Authorization'] = 'Bearer ' + token
    if duty: h['X-Duty-Token'] = duty
    data = json.dumps(body).encode() if body is not None else (b'{}' if method in ('POST', 'PUT') else None)
    req = urllib.request.Request(api.BASE + path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req) as r:
            return r.status, json.loads(r.read() or b'null')
    except urllib.error.HTTPError as e:
        t = e.read()
        try: return e.code, json.loads(t)
        except Exception: return e.code, {'raw': t[:300].decode('utf-8', 'replace')}


def login(who):
    return api.login(who['loginId'], who['password'])[0]


admin, principal, teacher = login(st['admin']), login(st['principal']), login(st['teacher'])
student, d1, d2 = login(st['student']), login(st['driverLogins']['D1']), login(st['driverLogins']['D2'])
S1 = st['student']['studentId']
A1, A2, A3 = st['areas']['A1'], st['areas']['A2'], st['areas']['A3']
D1, D2 = st['drivers']['D1'], st['drivers']['D2']


def area(a, pickup=None, drop=None):
    return {'areaId': a, 'morningPickupTime': pickup, 'afternoonDropTime': drop}


def put_driver(token, driver_id, areas, extra=None):
    """Rebuilds the full request from the driver's current detail, then replaces ONLY the areas (the one thing under test)."""
    cur = raw('GET', f'/api/Drivers/{driver_id}', admin)[1]['data']
    body = {'fullName': cur['fullName'], 'mobileNumber': cur['mobileNumber'], 'email': cur['email'], 'address': cur['address'], 'dlNumber': cur['dlNumber'],
            'dlValidUpto': cur['dlValidUpto'], 'vehicleId': cur['vehicleId'], 'areas': areas, **(extra or {})}
    return raw('PUT', f'/api/Drivers/{driver_id}', token, body)


def db_times(driver, a):
    return sql(f"select coalesce(morning_pickup_time::text,'NULL')||'|'||coalesce(afternoon_drop_time::text,'NULL') from tb_driver_areas where driver_id='{driver}' and area_id={a}").strip()


def link_ids(driver):
    return sql(f"select string_agg(driver_area_id::text, ',' order by driver_area_id) from tb_driver_areas where driver_id='{driver}'").strip()


def times_of(driver_dto, a):
    x = next(i for i in driver_dto['areas'] if i['areaId'] == a)
    return x['morningPickupTime'], x['afternoonDropTime']


print('== the columns exist, existing links are NULL')
chk('both columns are time without time zone, nullable', sql("select count(*) from information_schema.columns where table_name='tb_driver_areas' and column_name in ('morning_pickup_time','afternoon_drop_time') and data_type='time without time zone' and is_nullable='YES'").strip() == '2')
chk('links created before any time was set keep NULL in both', db_times(D1, A1) == 'NULL|NULL' and db_times(D2, A3) == 'NULL|NULL')
s, j = raw('GET', '/api/Transport/my-bus', student)
chk('my-bus (not assigned) has an empty scheduled[]', s == 200 and j['data']['scheduled'] == [] and j['data']['assigned'] is False)

print('== create / update / clear (School Admin)')
before_ids = link_ids(D1)
s, j = put_driver(admin, D1, [area(A1, '07:30', '14:15'), area(A2, '06:00', '13:00')])
chk('set times on both of D1\'s areas: 200', s == 200, f'{s} {j}')
chk('  answered in HH:mm', s == 200 and times_of(j['data'], A1) == ('07:30', '14:15') and times_of(j['data'], A2) == ('06:00', '13:00'))
chk('  stored as time values in the database', db_times(D1, A1) == '07:30:00|14:15:00' and db_times(D1, A2) == '06:00:00|13:00:00', db_times(D1, A1))
chk('  the SAME link rows (nothing deleted and re-created)', link_ids(D1) == before_ids, f'{before_ids} -> {link_ids(D1)}')
s, j = put_driver(admin, D1, [area(A1, None, '14:15'), area(A2, '06:00', '13:00')])
chk('clear one time: 200, that time is NULL, the other stays', s == 200 and times_of(j['data'], A1) == (None, '14:15') and db_times(D1, A1) == 'NULL|14:15:00')
s, j = put_driver(admin, D1, [area(A1), area(A2)])
chk('clear both on both areas: NULL|NULL', s == 200 and db_times(D1, A1) == 'NULL|NULL' and db_times(D1, A2) == 'NULL|NULL')
s, j = put_driver(admin, D1, [area(A1, '07:30', '14:15'), area(A2, '06:00', '13:00')])
chk('set them again', s == 200 and db_times(D1, A2) == '06:00:00|13:00:00')

print('== invalid input is refused and writes nothing')
snapshot = (db_times(D1, A1), db_times(D1, A2), link_ids(D1))
bad = {
    'hour 25': [area(A1, '25:00', None)], 'minute 60': [area(A1, '07:60', None)], 'no leading zero': [area(A1, '7:30', None)], 'seconds': [area(A1, '07:30:00', None)],
    'words': [area(A1, 'soon', None)], 'drop malformed': [area(A1, None, '14:3')],
    'pickup after drop': [area(A1, '15:00', '07:00')], 'pickup equals drop': [area(A1, '07:30', '07:30')],
    'area listed twice': [area(A1), area(A1, '07:00', None)], 'missing areaId': [{'morningPickupTime': '07:00'}], 'areaId zero': [area(0)],
}
for name, areas in bad.items():
    s, j = put_driver(admin, D1, areas)
    text = json.dumps(j)
    chk(f'{name}: 400 with a plain message', s == 400 and j.get('status') is False and j.get('data') is None and not any(w in text for w in ('Exception', 'Npgsql', ' at smss', 'SELECT', 'INSERT', 'stack')), f'{s} {text[:160]}')
s, j = put_driver(admin, D1, [area(A1, '07:30', None)], {'areaIds': [A1]})
chk('both shapes (areas AND areaIds): 400', s == 400 and 'areaIds' in j['message'], f'{s} {j}')
s, j = raw('PUT', f'/api/Drivers/{D1}', admin, {'fullName': 'x', 'mobileNumber': '9000000099', 'address': 'x', 'dlNumber': 'ZZDL-2020-0001', 'dlValidUpto': '2040-01-01', 'areaIds': [A1]})
chk('the OLD shape alone: 400 (never silently clears the areas)', s == 400 and 'areaIds' in j['message'], f'{s} {j}')
chk('nothing changed after all the refused calls', (db_times(D1, A1), db_times(D1, A2), link_ids(D1)) == snapshot)
chk('unknown area -> 404, another school style id -> 404', put_driver(admin, D1, [area(99999999, '07:00', None)])[0] == 404)

print('== the database CHECK is the backstop')
def refused(label, statement):
    try:
        devdb.psql(statement)
        chk(label, False, 'the database accepted it')
    except RuntimeError as e:
        chk(label, 'ck_tb_driver_areas_times_order' in str(e), str(e)[:140])
rid = sql(f"select driver_area_id from tb_driver_areas where driver_id='{D1}' and area_id={A1}").strip()
refused('CHECK refuses pickup later than drop', f"update tb_driver_areas set morning_pickup_time='15:00', afternoon_drop_time='07:00' where driver_area_id={rid}")
refused('CHECK refuses pickup equal to drop', f"update tb_driver_areas set morning_pickup_time='07:30', afternoon_drop_time='07:30' where driver_area_id={rid}")
try:
    devdb.psql(f"begin; update tb_driver_areas set morning_pickup_time='08:00', afternoon_drop_time=null where driver_area_id={rid}; rollback;")
    chk('CHECK allows a NULL on either side (rolled back)', True)
except RuntimeError as e:
    chk('CHECK allows a NULL on either side (rolled back)', False, str(e)[:140])
chk('the failed statements changed nothing', db_times(D1, A1) == '07:30:00|14:15:00')

print('== authorization: School Admin writes, Principal reads')
for who, tok in [('Principal', principal), ('Teacher', teacher), ('Student', student), ('Driver', d1)]:
    chk(f'{who} PUT /Drivers is 403', put_driver(tok, D1, [area(A1, '05:00', '06:00')])[0] == 403)
chk('no times were written by the refused calls', db_times(D1, A1) == '07:30:00|14:15:00')
s, j = raw('GET', f'/api/Drivers/{D1}', principal)
chk('Principal GET /Drivers/{id} is 200 and shows the times', s == 200 and times_of(j['data'], A1) == ('07:30', '14:15'))
for who, tok in [('Teacher', teacher), ('Student', student)]:
    chk(f'{who} GET /Drivers/{{id}} is 403', raw('GET', f'/api/Drivers/{D1}', tok)[0] == 403)

print('== driver app: GET /Drivers/me shows the driver their OWN times only')
put_driver(admin, D2, [area(A2, '06:30', '13:30'), area(A3, '09:00', None)])
s, j = raw('GET', '/api/Drivers/me', d1)
mine = {a['areaId']: (a['morningPickupTime'], a['afternoonDropTime']) for a in j['data']['areas']}
chk('D1 sees its own times for A1 and A2', s == 200 and mine == {A1: ('07:30', '14:15'), A2: ('06:00', '13:00')}, str(mine))
chk('  nothing of D2 (06:30 / 13:30 / 09:00) in the answer', not any(x in json.dumps(j) for x in ('06:30', '13:30', '09:00')))
s, j = raw('GET', '/api/Drivers/me', d2)
chk('D2 sees its own times', s == 200 and {a['areaId']: (a['morningPickupTime'], a['afternoonDropTime']) for a in j['data']['areas']} == {A2: ('06:30', '13:30'), A3: ('09:00', None)})

print('== student view: only my own area, two drivers on one area, exactly one list per driver')
def assign(a): return raw('PUT', f'/api/StudentTransport/{S1}', admin, {'areaId': a})[0]
def mybus(): return raw('GET', '/api/Transport/my-bus', student)[1]['data']
chk('assign the student to Civil Lines (A1)', assign(A1) in (200, 201))
b = mybus()
chk('A1: nobody on duty, scheduled[] holds D1 only with D1\'s A1 times', b['buses'] == [] and len(b['scheduled']) == 1 and b['scheduled'][0]['driverName'] == 'ZZ Ramesh Kumar' and (b['scheduled'][0]['morningPickupTime'], b['scheduled'][0]['afternoonDropTime']) == ('07:30', '14:15'), json.dumps(b['scheduled']))
chk('  keys are exactly the documented ones, no status, no position, no ids', sorted(b['scheduled'][0].keys()) == ['afternoonDropTime', 'driverName', 'morningPickupTime', 'vehicleLabel', 'vehiclePlate'])
chk('  the "no bus on duty" line is still there', 'No bus is on duty' in (b['message'] or ''))
text = json.dumps(b)
chk('  none of D1\'s Ring Road times (06:00 / 13:00), none of D2\'s, no phone/licence/driver id', not any(x in text for x in ('06:00', '13:00', '06:30', '13:30', '09:00', '9000000011', 'ZZDL', D1, D2)), text[:200])

chk('move the student to Ring Road (A2)', assign(A2) == 200)
b = mybus()
chk('A2: TWO drivers cover it, each with their own times', sorted((x['driverName'], x['morningPickupTime'], x['afternoonDropTime']) for x in b['scheduled']) == [('ZZ Ramesh Kumar', '06:00', '13:00'), ('ZZ Suresh Yadav', '06:30', '13:30')], json.dumps(b['scheduled']))

duty = raw('POST', '/api/DriverDuty/start', d1)[1]['data']['trackingToken']
fix = {'latitude': 26.85, 'longitude': 80.95, 'accuracyMeters': 8, 'speedMps': 6, 'headingDegrees': 90, 'recordedAt': __import__('datetime').datetime.now(__import__('datetime').timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.%f')[:-3] + 'Z'}
raw('POST', '/api/DriverDuty/location', None, fix, duty)
b = mybus()
chk('D1 on duty: D1 is in buses[] with its A2 times, and ONLY D2 is in scheduled[]',
    [(x['driverName'], x['morningPickupTime'], x['afternoonDropTime'], x['status']) for x in b['buses']] == [('ZZ Ramesh Kumar', '06:00', '13:00', 'LIVE')] and [x['driverName'] for x in b['scheduled']] == ['ZZ Suresh Yadav'], json.dumps(b))
names = [x['driverName'] for x in b['buses']] + [x['driverName'] for x in b['scheduled']]
chk('  no driver appears in both lists', len(names) == len(set(names)))
raw('POST', '/api/DriverDuty/end', None, None, duty)
b = mybus()
chk('after End duty D1 moves back to scheduled[]', b['buses'] == [] and len(b['scheduled']) == 2)

chk('move the student to Old City (A3)', assign(A3) == 200)
b = mybus()
chk('A3: only D2, with its A3 times (pickup only)', [(x['driverName'], x['morningPickupTime'], x['afternoonDropTime']) for x in b['scheduled']] == [('ZZ Suresh Yadav', '09:00', None)], json.dumps(b['scheduled']))

print('== clearing a time removes it for the student; a driver with no time at all is not listed')
put_driver(admin, D2, [area(A2, '06:30', '13:30'), area(A3)])
b = mybus()
chk('D2 has no time left on A3: scheduled[] is empty (a schedule line without a time is noise)', b['scheduled'] == [], json.dumps(b['scheduled']))

print('== a deactivated driver is not scheduled')
chk('student back on A2', assign(A2) == 200)
toggled = raw('POST', f'/api/Drivers/{D2}/toggle-status', admin)[0]
b = mybus()
chk('D2 inactive: only D1 remains in scheduled[]', toggled == 200 and [x['driverName'] for x in b['scheduled']] == ['ZZ Ramesh Kumar'], json.dumps(b['scheduled']))
raw('POST', f'/api/Drivers/{D2}/toggle-status', admin)

print()
fails = [r for r in rows if not r[1]]
print(f'{len(rows)} checks, {len(fails)} failed')
for r in fails: print('  FAILED:', r[0], r[2])
