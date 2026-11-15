import json, os, urllib.request, urllib.error
SP = os.path.dirname(os.path.abspath(__file__))
BASE = 'http://127.0.0.1:5292'


def session():
    return json.load(open(os.path.join(SP, 'session.json')))


def save(st):
    json.dump(st, open(os.path.join(SP, 'session.json'), 'w'), indent=1)


def call(method, path, token=None, body=None, headers=None, raw=None, ctype='application/json'):
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else (b'{}' if method in ('POST', 'PUT') else None))
    h = {'Content-Type': ctype, **({'Authorization': 'Bearer ' + token} if token else {}), **(headers or {})}
    req = urllib.request.Request(BASE + path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req) as r:
            return r.status, json.loads(r.read() or b'null')
    except urllib.error.HTTPError as e:
        t = e.read()
        try:
            return e.code, json.loads(t)
        except Exception:
            return e.code, t[:300]


def ok(res, what, want=(200, 201)):
    s, j = res
    if s not in want:
        raise SystemExit(f'FAILED {what}: {s} {json.dumps(j)[:600]}')
    return j['data'] if isinstance(j, dict) and 'data' in j else j


def login(user, password):
    d = ok(call('POST', '/api/Auth/login', body={'loginId': user, 'password': password, 'rememberMe': False}), 'login ' + user)
    return d['token'], d['user']
