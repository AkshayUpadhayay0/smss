"""Simulated driver: signs in, starts duty, then drives a looped route sending a fix every few seconds, like the driver app would.
usage: python sim_driver.py D1|D2 [seconds-between-fixes]
Pause sending (to watch LIVE -> STALE -> OFFLINE): create the file pause_<D>.flag next to this script; delete it to carry on. Stop: kill the process, or create stop_<D>.flag (ends duty politely)."""
import datetime, json, math, os, sys, time
import api

key = sys.argv[1]
step = float(sys.argv[2]) if len(sys.argv) > 2 else 5.0
SP = os.path.dirname(os.path.abspath(__file__))
st = api.session()
login = st['driverLogins'][key]
tok, _ = api.login(login['loginId'], login['password'])

# two loops around Lucknow city (about 3 km each), different centres
CENTRE = {'D1': (26.8467, 80.9462, 0.012, 0.014), 'D2': (26.8800, 80.9800, 0.010, 0.012)}[key]
POINTS = 160


def point(i):
    a = 2 * math.pi * (i % POINTS) / POINTS
    lat0, lng0, rlat, rlng = CENTRE
    wobble = 1 + 0.12 * math.sin(3 * a)
    return lat0 + rlat * wobble * math.sin(a), lng0 + rlng * wobble * math.cos(a)


def heading(i):
    (la1, lo1), (la2, lo2) = point(i), point(i + 1)
    return (math.degrees(math.atan2(lo2 - lo1, la2 - la1)) + 360) % 360


def now_iso():
    return datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%S.%f')[:-3] + 'Z'


s, j = api.call('POST', '/api/DriverDuty/start', tok)
print('start', s, j.get('message') if isinstance(j, dict) else j, flush=True)
if s not in (200, 201):
    sys.exit(1)
token = j['data']['trackingToken']
i = 0 if key == 'D1' else 40
while True:
    if os.path.exists(os.path.join(SP, f'stop_{key}.flag')):
        print('end', api.call('POST', '/api/DriverDuty/end', None, None, {'X-Duty-Token': token})[0], flush=True)
        break
    if not os.path.exists(os.path.join(SP, f'pause_{key}.flag')):
        la, lo = point(i)
        s, j = api.call('POST', '/api/DriverDuty/location', None, {'latitude': round(la, 6), 'longitude': round(lo, 6), 'accuracyMeters': 8, 'speedMps': 8 + 2 * math.sin(i / 7),
                                                                  'headingDegrees': round(heading(i), 1), 'recordedAt': now_iso()}, {'X-Duty-Token': token})
        if s == 429:
            time.sleep(2)
            continue
        if s != 200:
            print('stopped:', s, j, flush=True)
            break
        i += 1
    time.sleep(step)
