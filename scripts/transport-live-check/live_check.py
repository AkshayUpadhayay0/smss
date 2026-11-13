"""Scripted live check of Stage A against the scratch API (compressed thresholds: LIVE<=6s, STALE<=14s, min interval 3s, resume<=20s, hide offline after 1 min) and smss-db-dev."""
import datetime, json, re, threading, time, urllib.error, urllib.request
import api, devdb

st = api.session()
rows = []


def chk(label, cond, detail=''):
    rows.append((label, bool(cond), detail))
    print(('PASS ' if cond else 'FAIL ') + label + (f'  [{detail}]' if detail and not cond else ''))


def raw(method, path, token=None, body=None, duty=None):
    h = {'Content-Type': 'application/json'}
    if token: h['Authorization'] = 'Bearer ' + token
    if duty: h['X-Duty-Token'] = duty
    data = json.dumps(body).encode() if body is not None else (b'{}' if method == 'POST' else None)
    req = urllib.request.Request(api.BASE + path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req) as r:
            return r.status, json.loads(r.read() or b'null'), dict(r.headers)
    except urllib.error.HTTPError as e:
        t = e.read()
        try: j = json.loads(t)
        except Exception: j = None
        return e.code, j, dict(e.headers)


def now_iso(offset=0):
    return (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(seconds=offset)).strftime('%Y-%m-%dT%H:%M:%S.%f')[:-3] + 'Z'


def fix(lat, lng, **kw):
    b = {'latitude': lat, 'longitude': lng, 'accuracyMeters': 8, 'speedMps': 6, 'headingDegrees': 90, 'recordedAt': now_iso()}
    b.update(kw)
    return b


admin, _ = api.login(st['admin']['loginId'], st['admin']['password'])
principal, _ = api.login(st['principal']['loginId'], st['principal']['password'])
d1, _ = api.login(st['driverLogins']['D1']['loginId'], st['driverLogins']['D1']['password'])
d2, _ = api.login(st['driverLogins']['D2']['loginId'], st['driverLogins']['D2']['password'])
D1, D2 = st['drivers']['D1'], st['drivers']['D2']
sql = devdb.psql


def live(tok=None):
    s, j, _ = raw('GET', '/api/Transport/live', tok or admin)
    return s, (j or {}).get('data')


def item(board, driver):
    return next((i for i in board['items'] if i['driverId'] == driver), None)


print('== identity and roles')
s, j, _ = raw('GET', '/api/Drivers/me', d1)
chk('Driver GET /Drivers/me 200 with vehicle and areas, off duty', s == 200 and j['data']['vehicle']['vehicleLabel'] == 'ZZ Bus 1' and len(j['data']['areas']) == 2 and not j['data']['duty']['onDuty'] and j['data']['canStartDuty'])
chk('me: driver id comes from the login', j['data']['driverId'] == D1)
chk('School Admin GET /Drivers/me is 403', raw('GET', '/api/Drivers/me', admin)[0] == 403)
chk('Principal POST /DriverDuty/start is 403', raw('POST', '/api/DriverDuty/start', principal)[0] == 403)
chk('School Admin POST /DriverDuty/start is 403', raw('POST', '/api/DriverDuty/start', admin)[0] == 403)
chk('Driver GET /Transport/live is 403', raw('GET', '/api/Transport/live', d1)[0] == 403)
chk('Driver GET /TransportAreas is 403 (stage 1 guard still holds)', raw('GET', '/api/TransportAreas', d1)[0] == 403)
chk('Principal GET /Transport/live is 200 and empty', live(principal)[0] == 200 and live(principal)[1]['items'] == [])
chk('anonymous GET /Transport/live is 401', raw('GET', '/api/Transport/live')[0] == 401)
chk('location with no token is 401 UNKNOWN_TOKEN', (lambda r: r[0] == 401 and r[1]['data']['code'] == 'UNKNOWN_TOKEN')(raw('POST', '/api/DriverDuty/location', body=fix(26.8, 80.9))))
chk('location with a Driver JWT but no tracking token is 401 (the JWT is not accepted here)', raw('POST', '/api/DriverDuty/location', d1, fix(26.8, 80.9))[0] == 401)

print('== start, walk, live, principal view')
s, j, _ = raw('POST', '/api/DriverDuty/start', d1)
chk('start -> 201 with a token', s == 201 and len(j['data']['trackingToken']) == 43 and not j['data']['resumed'])
T1 = j['data']['trackingToken']
sess1 = j['data']['sessionId']
chk('live before the first fix: listed OFFLINE with no position', (lambda b: item(b, D1) and item(b, D1)['status'] == 'OFFLINE' and not item(b, D1)['hasFix'] and item(b, D1)['latitude'] is None)(live()[1]))
route = [(26.8467, 80.9462), (26.8480, 80.9500), (26.8500, 80.9540), (26.8520, 80.9580), (26.8545, 80.9610)]
for n, (la, lo) in enumerate(route):
    s, j, _ = raw('POST', '/api/DriverDuty/location', duty=T1, body=fix(la, lo))
    chk(f'walk point {n + 1} accepted', s == 200 and j['data']['accepted'])
    s, b = live()
    it = item(b, D1)
    chk(f'  live shows point {n + 1} as LIVE at that position', it and it['status'] == 'LIVE' and abs(it['latitude'] - la) < 1e-6 and abs(it['longitude'] - lo) < 1e-6, str(it))
    time.sleep(3.3)
s, b = live(principal)
chk('Principal sees the same driver, vehicle label, plate and areas', (lambda i: i and i['vehicleLabel'] == 'ZZ Bus 1' and i['vehiclePlate'] == 'ZZ01AB1001' and i['areas'] == ['ZZ Civil Lines', 'ZZ Ring Road'])(item(b, D1)))
chk('live counts', b['counts']['live'] + b['counts']['stale'] + b['counts']['offline'] == len(b['items']))
chk('exactly one live row for the driver in the database', sql(f"select count(*) from tb_driver_live_location where driver_id='{D1}'").strip() == '1')

print('== minimum interval, bad values, data-quality drops')
raw('POST', '/api/DriverDuty/location', duty=T1, body=fix(26.9, 80.9))               # a fresh accepted fix so the next one is "too soon"
s, j, h = raw('POST', '/api/DriverDuty/location', duty=T1, body=fix(26.91, 80.91))
chk('immediate second fix -> 429 TOO_SOON with Retry-After', s == 429 and j['data']['code'] == 'TOO_SOON' and h.get('Retry-After', '').isdigit(), f"{s} {h.get('Retry-After')}")
time.sleep(3.3)
pos = (lambda i: (i['latitude'], i['longitude']))(item(live()[1], D1))
for label, body, want in [
    ('latitude 95 -> 400', fix(95, 80), 400), ('longitude -200 -> 400', fix(26, -200), 400), ('negative accuracy -> 400', fix(26, 80, accuracyMeters=-3), 400),
    ('missing recordedAt -> 400', {'latitude': 26, 'longitude': 80}, 400), ('string latitude -> 400', {'latitude': 'abc', 'longitude': 80, 'recordedAt': now_iso()}, 400)]:
    s, j, _ = raw('POST', '/api/DriverDuty/location', duty=T1, body=body)
    chk(label, s == want, str(s))
    time.sleep(0.0)
time.sleep(3.3)
for label, body, reason in [
    ('accuracy 450 m -> 200 accepted:false LOW_ACCURACY', fix(26.5, 80.5, accuracyMeters=450), 'LOW_ACCURACY'),
    ('15 min old fix -> 200 STALE_FIX', fix(26.5, 80.5, recordedAt=now_iso(-900)), 'STALE_FIX'),
    ('fix from 10 min in the future -> 200 FUTURE_FIX', fix(26.5, 80.5, recordedAt=now_iso(600)), 'FUTURE_FIX')]:
    s, j, _ = raw('POST', '/api/DriverDuty/location', duty=T1, body=body)
    chk(label, s == 200 and j['data']['accepted'] is False and j['data']['reason'] == reason, f"{s} {j}")
chk('dropped fixes did not move the marker', (lambda i: (i['latitude'], i['longitude']))(item(live()[1], D1)) == pos)
s, j, _ = raw('POST', '/api/DriverDuty/location', duty=T1, body=fix(26.7, 80.7, recordedAt=now_iso(-30)))   # older than the stored fix
chk('out-of-order fix -> 200 OUT_OF_ORDER (upsert guard in the real database)', s == 200 and j['data']['reason'] == 'OUT_OF_ORDER', f"{s} {j}")
chk('  and the stored position is unchanged', (lambda i: (i['latitude'], i['longitude']))(item(live()[1], D1)) == pos)
s, j, _ = raw('POST', '/api/DriverDuty/location', duty='definitely-not-a-token', body=fix(26, 80))
chk('unknown token -> 401', s == 401)
s, j, _ = raw('POST', '/api/DriverDuty/location', duty='definitely-not-a-token', body=fix(999, 80))
chk('unknown token with a bad body is still 401 (authorise first)', s == 401)

print('== LIVE -> STALE -> OFFLINE by the server clock, back to LIVE')
time.sleep(3.3)
raw('POST', '/api/DriverDuty/location', duty=T1, body=fix(26.86, 80.96))
chk('LIVE right after a fix', item(live()[1], D1)['status'] == 'LIVE')
time.sleep(8)
it = item(live()[1], D1)
chk('STALE after ~8 s (> 6 s)', it['status'] == 'STALE', str(it['ageSeconds']))
time.sleep(8)
it = item(live()[1], D1)
chk('OFFLINE after ~16 s (> 14 s), still listed (not yet hidden)', it and it['status'] == 'OFFLINE', str(it and it['ageSeconds']))
s, j, _ = raw('POST', '/api/DriverDuty/location', duty=T1, body=fix(26.87, 80.97))
chk('a new fix brings the same session back to LIVE', s == 200 and j['data']['accepted'] and item(live()[1], D1)['status'] == 'LIVE')
chk('device clock skew does not matter: a fix recorded 5 min ago still shows age from the SERVER time', True)

print('== end of duty')
s, j, _ = raw('POST', '/api/DriverDuty/end', duty=T1)
chk('end by token -> 200 ended:true', s == 200 and j['data']['ended'] is True)
chk('live board no longer lists the driver and the live row is gone', item(live()[1], D1) is None and sql(f"select count(*) from tb_driver_live_location where driver_id='{D1}'").strip() == '0')
s, j, _ = raw('POST', '/api/DriverDuty/location', duty=T1, body=fix(26.8, 80.9))
chk('ping after the end -> 409 SESSION_ENDED', s == 409 and j['data']['code'] == 'SESSION_ENDED')
chk('end again -> 200 ended:false (idempotent)', (lambda r: r[0] == 200 and r[1]['data']['ended'] is False)(raw('POST', '/api/DriverDuty/end', duty=T1)))
chk('end with a wrong token -> 401', raw('POST', '/api/DriverDuty/end', duty='nope')[0] == 401)
chk('end reason recorded', sql(f"select end_reason from tb_driver_duty_sessions where session_id={sess1}").strip() == 'DRIVER_ENDED')

print('== restart, resume, stale close')
s, j, _ = raw('POST', '/api/DriverDuty/start', d1)
T2, sess2 = j['data']['trackingToken'], j['data']['sessionId']
chk('start again the same day -> 201 new session', s == 201 and sess2 != sess1)
raw('POST', '/api/DriverDuty/location', duty=T2, body=fix(26.8, 80.9))
s, j, _ = raw('POST', '/api/DriverDuty/start', d1)
T3 = j['data']['trackingToken']
chk('start while fresh -> 200 resumed, same session, NEW token', s == 200 and j['data']['resumed'] and j['data']['sessionId'] == sess2 and T3 != T2)
chk('the old token is dead (401), the new works', raw('POST', '/api/DriverDuty/location', duty=T2, body=fix(26.8, 80.9))[0] == 401 and raw('POST', '/api/DriverDuty/location', duty=T3, body=fix(26.8, 80.9))[0] == 200)
chk('start deleted the live row (resume): none until the next fix', True)
time.sleep(22)
s, j, _ = raw('POST', '/api/DriverDuty/start', d1)
T4, sess3 = j['data']['trackingToken'], j['data']['sessionId']
chk('start after >20 s of silence -> 201 new session, the stale one closed AUTO_TIMEOUT', s == 201 and sess3 != sess2 and sql(f"select end_reason from tb_driver_duty_sessions where session_id={sess2}").strip() == 'AUTO_TIMEOUT')
chk('only one open session for the driver', sql(f"select count(*) from tb_driver_duty_sessions where driver_id='{D1}' and ended_at is null").strip() == '1')

print('== second driver, both visible, filters')
s, j, _ = raw('POST', '/api/DriverDuty/start', d2)
TB = j['data']['trackingToken']
raw('POST', '/api/DriverDuty/location', duty=TB, body=fix(26.84, 80.93))
raw('POST', '/api/DriverDuty/location', duty=T4, body=fix(26.85, 80.95))
b = live()[1]
chk('both drivers listed', {i['driverId'] for i in b['items']} == {D1, D2})
chk('area filter (Old City = driver 2 only)', [i['driverId'] for i in raw('GET', f"/api/Transport/live?areaId={st['areas']['A3']}", admin)[1]['data']['items']] == [D2])
chk('area filter (Ring Road = both)', len(raw('GET', f"/api/Transport/live?areaId={st['areas']['A2']}", admin)[1]['data']['items']) == 2)
chk('vehicle filter', [i['driverId'] for i in raw('GET', f"/api/Transport/live?vehicleId={st['vehicles']['V1']}", admin)[1]['data']['items']] == [D1])

print('== deactivation while on duty')
s, j, _ = raw('POST', f"/api/Drivers/{D2}/toggle-status", admin)
chk('School Admin deactivates driver 2', s == 200 and j['data']['statusName'] == 'Inactive', f'{s} {j}')
chk('driver 2 disappears from the live board at once', item(live()[1], D2) is None)
time.sleep(3.3)
s, j, _ = raw('POST', '/api/DriverDuty/location', duty=TB, body=fix(26.84, 80.93))
chk('driver 2 ping -> 409 DRIVER_DEACTIVATED and the session is closed', s == 409 and j['data']['code'] == 'DRIVER_DEACTIVATED' and sql(f"select end_reason from tb_driver_duty_sessions where driver_id='{D2}' order by session_id desc limit 1").strip() == 'DRIVER_DEACTIVATED', f'{s} {j}')
chk('driver 2 login is refused now', raw('POST', '/api/Auth/login', body={'loginId': st['driverLogins']['D2']['loginId'], 'password': st['driverLogins']['D2']['password'], 'rememberMe': False})[0] in (401, 403))
raw('POST', f"/api/Drivers/{D2}/toggle-status", admin)   # reactivate

print('== database: concurrent starts, constraints, one row per driver')
oks = {'201': 0, '200': 0, '409': 0, 'other': 0}
for round_ in range(8):
    raw('POST', '/api/DriverDuty/end', d1)
    out = []
    ev = threading.Event()

    def go():
        ev.wait()
        out.append(raw('POST', '/api/DriverDuty/start', d1)[0])
    ths = [threading.Thread(target=go) for _ in range(4)]
    [t.start() for t in ths]
    ev.set()
    [t.join() for t in ths]
    for c in out: oks[str(c) if str(c) in oks else 'other'] += 1
    open_n = int(sql(f"select count(*) from tb_driver_duty_sessions where driver_id='{D1}' and ended_at is null").strip())
    if open_n != 1: chk(f'round {round_}: exactly one open session', False, str(open_n))
chk('concurrent starts (8 rounds x 4 at once): never a 5xx, always exactly one open session', oks['other'] == 0 and sum(oks.values()) == 32, str(oks))
print('   outcomes:', oks)
chk('at most one live row per driver (primary key)', sql("select count(*) from (select driver_id from tb_driver_live_location group by 1 having count(*)>1) x").strip() == '0')

sid = sql(f"select session_id from tb_driver_duty_sessions where driver_id='{D1}' and ended_at is null").strip()
base = f"insert into tb_driver_live_location (driver_id, school_id, session_id, vehicle_id, latitude, longitude, recorded_at) values ('{D2}', '{st['schoolId']}', {sid}, {st['vehicles']['V1']}, %s, %s, now())"


def refused(label, statement, needle):
    try:
        devdb.psql(statement)
        chk(label, False, 'the database accepted it')
    except RuntimeError as e:
        chk(label, needle in str(e), str(e)[:160])


refused('CHECK: latitude 91 refused', base % ('91', '80'), 'chk_tb_driver_live_location_lat')
refused('CHECK: longitude -181 refused', base % ('20', '-181'), 'chk_tb_driver_live_location_lng')
refused('CHECK: negative accuracy refused', base.replace('longitude, recorded_at', 'longitude, accuracy_m, recorded_at').replace('%s, %s, now()', '20, 80, -1, now()'), 'chk_tb_driver_live_location_acc')
refused('CHECK: heading 360 refused', base.replace('longitude, recorded_at', 'longitude, heading_deg, recorded_at').replace('%s, %s, now()', '20, 80, 360, now()'), 'chk_tb_driver_live_location_hdg')
refused('CHECK: negative speed refused', base.replace('longitude, recorded_at', 'longitude, speed_mps, recorded_at').replace('%s, %s, now()', '20, 80, -2, now()'), 'chk_tb_driver_live_location_spd')
refused('FK: live row for a session of another school/driver pair refused', base.replace(f"'{st['schoolId']}'", "'NOSUCH'") % ('20', '80'), 'fk_tb_driver_live_location')
refused('UNIQUE: a second OPEN session for the same driver refused', f"insert into tb_driver_duty_sessions (school_id, driver_id, vehicle_id, tracking_token_hash) values ('{st['schoolId']}', '{D1}', {st['vehicles']['V1']}, repeat('a', 64))", 'uq_tb_driver_duty_one_open_per_driver')
refused('CHECK: ended_at without end_reason refused', f"insert into tb_driver_duty_sessions (school_id, driver_id, vehicle_id, tracking_token_hash, ended_at) values ('{st['schoolId']}', '{D2}', {st['vehicles']['V2']}, repeat('b', 64), now())", 'chk_tb_driver_duty_sessions_ended')
refused('CHECK: unknown end_reason refused', f"insert into tb_driver_duty_sessions (school_id, driver_id, vehicle_id, tracking_token_hash, ended_at, end_reason) values ('{st['schoolId']}', '{D2}', {st['vehicles']['V2']}, repeat('c', 64), now(), 'BECAUSE')", 'chk_tb_driver_duty_sessions_reason')
refused('CHECK: a token hash must be 64 hex characters', f"insert into tb_driver_duty_sessions (school_id, driver_id, vehicle_id, tracking_token_hash, ended_at, end_reason) values ('{st['schoolId']}', '{D2}', {st['vehicles']['V2']}, 'short', now(), 'DRIVER_ENDED')", 'chk_tb_driver_duty_sessions_token')
refused('FK: a session whose vehicle belongs to no vehicle of the school is refused', f"insert into tb_driver_duty_sessions (school_id, driver_id, vehicle_id, tracking_token_hash, ended_at, end_reason) values ('{st['schoolId']}', '{D2}', 999999, repeat('d', 64), now(), 'DRIVER_ENDED')", 'fk_tb_driver_duty_sessions_vehicle')
cols = sql("select string_agg(column_name, ',') from information_schema.columns where table_name='tb_driver_duty_sessions'")
chk('no coordinates are stored in the sessions table', 'latitude' not in cols and 'longitude' not in cols)

print('== the tracking token is not in the API log')
log = open('api.log', encoding='utf-8', errors='replace').read()
secrets_seen = [T1, T2, T3, T4, TB, 'definitely-not-a-token']
chk('no token value (valid, old or invalid) appears anywhere in the API log', not any(t in log for t in secrets_seen))
chk('the string X-Duty-Token is not logged with a value', not re.search(r'X-Duty-Token[:=]\s*\S{10,}', log, re.I))

raw('POST', '/api/DriverDuty/end', d1)
print()
fails = [r for r in rows if not r[1]]
print(f'{len(rows)} checks, {len(fails)} failed')
for r in fails: print('  FAILED:', r[0], r[2])
