"""Starts the scratch API (published copy) on :5292 against smss-db-dev only. Extra KEY=VALUE arguments become environment overrides (e.g. TransportLive__LiveSeconds=6)."""
import json, os, secrets, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import devdb

SP = os.path.dirname(os.path.abspath(__file__))
state_path = os.path.join(SP, 'session.json')
st = json.load(open(state_path)) if os.path.exists(state_path) else {}
st.setdefault('jwt', secrets.token_urlsafe(48))
st.setdefault('setupKey', secrets.token_urlsafe(24))
env = dict(os.environ,
           ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS='http://127.0.0.1:5292',
           ConnectionStrings__DefaultConnection=devdb.dev_connection_string(),
           Jwt__Key=st['jwt'],
           Bootstrap__Enabled='true', Bootstrap__SuperAdminSetupKey=st['setupKey'],
           Email__SmtpHost='127.0.0.1', Email__SmtpPort='1',            # no mail can leave this machine
           Cors__AllowedOrigins__0='http://localhost:4300', Cors__AllowedOrigins__1='http://127.0.0.1:4300',
           FileStorage__RootPath=os.path.join(SP, 'files', 'public'), FileStorage__PrivateRootPath=os.path.join(SP, 'files', 'private'))
for a in sys.argv[1:]:
    k, v = a.split('=', 1)
    env[k] = v
st['apiEnvOverrides'] = sys.argv[1:]
log = open(os.path.join(SP, 'api.log'), 'w')
p = subprocess.Popen([os.path.join(SP, 'pubapi', 'smss-api.exe')], cwd=os.path.join(SP, 'pubapi'), env=env, stdout=log, stderr=subprocess.STDOUT,
                     creationflags=0x00000008 | 0x00000200)   # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
st['apiPid'] = p.pid
json.dump(st, open(state_path, 'w'), indent=1)
print('api pid', p.pid)
