import json, os, re, ssl, subprocess, urllib.request, urllib.error
B = 'https://localhost:7037/api'
CTX = ssl.create_default_context(); CTX.check_hostname = False; CTX.verify_mode = ssl.CERT_NONE
PSQL = r'C:\OSGeo4W\bin\psql.exe'
def _pw():
    t = open(r'E:\05_Smss\smss\smss-api\smss-api\appsettings.json', encoding='utf-8').read()
    return re.search(r'Password=([^;"]*)', t).group(1)
def sql(q):
    r = subprocess.run([PSQL, '-h', 'localhost', '-U', 'postgres', '-d', 'smss-db', '-At', '-F', '|'], input=q, capture_output=True, text=True, env=dict(os.environ, PGPASSWORD=_pw()))
    return (r.stdout + r.stderr).strip()
def call(m, p, t=None, b=None):
    d = json.dumps(b).encode() if b is not None else (b'{}' if m in ('POST', 'PUT') else None)
    r = urllib.request.Request(B + p, method=m, data=d); r.add_header('Content-Type', 'application/json')
    if t: r.add_header('Authorization', 'Bearer ' + t)
    try:
        x = urllib.request.urlopen(r, timeout=120, context=CTX); raw = x.read(); return x.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as e:
        raw = e.read()
        try: return e.code, json.loads(raw)
        except Exception: return e.code, {'message': raw[:300].decode('utf8', 'ignore'), 'data': None}

class Sess:
    """Authenticated caller that transparently re-logs-in when the 15-min JWT expires."""
    def __init__(self, lid, pw): self.lid, self.pw, self.t = lid, pw, None; self.login()
    def login(self):
        c, j = call('POST', '/Auth/login', None, {'loginId': self.lid, 'password': self.pw})
        if c != 200: raise RuntimeError(f'login {self.lid}: {c} {j.get("message")}')
        self.t = j['data']['token']; self.user = j['data']['user']; return j['data']
    def __call__(self, m, p, b=None, ok=(200, 201), must=True):
        c, j = call(m, p, self.t, b)
        if c == 401: self.login(); c, j = call(m, p, self.t, b)
        if must and c not in ok: raise RuntimeError(f'{m} {p} -> {c} {j.get("message")} {json.dumps(b)[:300] if b else ""} {json.dumps(j.get("errors"))[:300] if j.get("errors") else ""}')
        return c, j
