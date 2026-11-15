"""Stage D cross-school isolation, live (scratch API + smss-db-dev ONLY). Run AFTER stage_a.py and live_check_d.py.
Registers a SECOND throwaway school (ZZDEMO02) and proves that its admin can neither read nor change the first school's driver times, that a driver of the
second school cannot be given the first school's area, and that its drivers never show up in the first school's student view. The second school's id is
recorded in session.json as 'schoolB' so cleanup.py can remove it too."""
import json, secrets, urllib.error, urllib.request
import api, devdb

st = api.session()
rows = []
sql = devdb.psql


def chk(label, cond, detail=''):
    rows.append((label, bool(cond), detail))
    print(('PASS ' if cond else 'FAIL ') + label + (f'  [{detail}]' if detail and not cond else ''))


def raw(method, path, token=None, body=None):
    h = {'Content-Type': 'application/json', **({'Authorization': 'Bearer ' + token} if token else {})}
    data = json.dumps(body).encode() if body is not None else (b'{}' if method in ('POST', 'PUT') else None)
    req = urllib.request.Request(api.BASE + path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req) as r:
            return r.status, json.loads(r.read() or b'null')
    except urllib.error.HTTPError as e:
        t = e.read()
        try: return e.code, json.loads(t)
        except Exception: return e.code, {'raw': t[:200].decode('utf-8', 'replace')}


def db_times(driver, a):
    return sql(f"select coalesce(morning_pickup_time::text,'NULL')||'|'||coalesce(afternoon_drop_time::text,'NULL') from tb_driver_areas where driver_id='{driver}' and area_id={a}").strip()


pw = 'Zz' + secrets.token_urlsafe(12) + '#9'
stok, _ = api.login(st['superAdmin']['loginId'], st['superAdmin']['password'])
reg = api.ok(api.call('POST', '/api/SchoolRegistration/register', stok, {
    'schoolCode': 'ZZDEMO02', 'schoolName': 'ZZ Second Transport School', 'schoolShortName': 'ZZ Two', 'email': 'zz-school2@example.invalid', 'mobileNumber': '9000000102',
    'addressLine1': 'ZZ Other Road', 'pincode': '226001',
    'contacts': [{'contactType': 'School Owner', 'contactName': 'ZZ Owner Two', 'email': 'zz-owner2@example.invalid', 'mobileNumber': '9000000103', 'isPrimary': True}]}), 'register school B')
sb = reg['school']['schoolId']
st['schoolB'] = sb
api.save(st)
t0, _ = api.login(sb, reg['temporaryPassword'])
api.ok(api.call('POST', '/api/Auth/change-password', t0, {'currentPassword': reg['temporaryPassword'], 'newPassword': pw}), 'admin B password', (200,))
adminB, _ = api.login(sb, pw)
adminA, _ = api.login(st['admin']['loginId'], st['admin']['password'])
studentA, _ = api.login(st['student']['loginId'], st['student']['password'])

areaB = api.ok(api.call('POST', '/api/TransportAreas', adminB, {'areaName': 'ZZ Two Area', 'distanceKm': 4, 'transportFee': 300}), 'area B')['areaId']
drvB = api.ok(api.call('POST', '/api/Drivers', adminB, {'fullName': 'ZZ Two Driver', 'mobileNumber': '9000000111', 'address': 'ZZ Other Road', 'dlNumber': 'ZZDL-2021-0777', 'dlValidUpto': '2040-01-01',
                                                          'areas': [{'areaId': areaB, 'morningPickupTime': '05:00', 'afternoonDropTime': '12:00'}]}), 'driver B')['driverId']
A1, D1 = st['areas']['A1'], st['drivers']['D1']
S1 = st['student']['studentId']
body = lambda areas: {'fullName': 'ZZ Hijack', 'mobileNumber': '9000000112', 'address': 'x', 'dlNumber': 'ZZDL-2020-0001', 'dlValidUpto': '2040-01-01', 'areas': areas}

print('== school B cannot read or change school A\'s driver times')
before = db_times(D1, A1)
chk('B admin GET of A\'s driver is 404', raw('GET', f'/api/Drivers/{D1}', adminB)[0] == 404)
chk('B admin PUT of A\'s driver (with A\'s area) is 404', raw('PUT', f'/api/Drivers/{D1}', adminB, body([{'areaId': A1, 'morningPickupTime': '01:00', 'afternoonDropTime': '02:00'}]))[0] == 404)
chk('B admin PUT of A\'s driver (with B\'s area) is 404', raw('PUT', f'/api/Drivers/{D1}', adminB, body([{'areaId': areaB, 'morningPickupTime': '01:00', 'afternoonDropTime': '02:00'}]))[0] == 404)
chk('A\'s times are unchanged in the database', db_times(D1, A1) == before, f'{before} -> {db_times(D1, A1)}')

print('== a school B driver cannot be given school A\'s area (any times)')
s, j = raw('PUT', f'/api/Drivers/{drvB}', adminB, body([{'areaId': A1, 'morningPickupTime': '07:00', 'afternoonDropTime': '14:00'}]) | {'dlNumber': 'ZZDL-2021-0777', 'fullName': 'ZZ Two Driver'})
chk('B driver with A\'s area is 404', s == 404, f'{s} {j}')
chk('B created no link to A\'s area', sql(f"select count(*) from tb_driver_areas where driver_id='{drvB}' and area_id={A1}").strip() == '0')
chk('B driver\'s own times are intact', db_times(drvB, areaB) == '05:00:00|12:00:00')
s, j = raw('POST', '/api/Drivers', adminB, body([{'areaId': A1, 'morningPickupTime': '07:00', 'afternoonDropTime': '14:00'}]) | {'dlNumber': 'ZZDL-2021-0778'})
chk('B creating a driver on A\'s area is 404 and creates nothing', s == 404 and sql(f"select count(*) from tb_drivers where school_id='{sb}'").strip() == '1', f'{s}')

print('== school A\'s student never sees school B')
raw('PUT', f'/api/StudentTransport/{S1}', adminA, {'areaId': A1})
b = raw('GET', '/api/Transport/my-bus', studentA)[1]['data']
text = json.dumps(b)
chk('A\'s student view has no school B driver, vehicle or time', 'ZZ Two' not in text and '05:00' not in text and '12:00' not in text, text[:200])
chk('B\'s admin cannot assign A\'s student', raw('PUT', f'/api/StudentTransport/{S1}', adminB, {'areaId': areaB})[0] == 404)
chk('B\'s admin cannot read A\'s student assignment', raw('GET', f'/api/StudentTransport/{S1}', adminB)[0] == 404)

print()
fails = [r for r in rows if not r[1]]
print(f'{len(rows)} checks, {len(fails)} failed')
for r in fails: print('  FAILED:', r[0], r[2])
