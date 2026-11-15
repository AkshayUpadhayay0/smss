"""Final cleanup of the ZZ throwaway data in smss-db-dev (never smss-db: devdb refuses any other database).
Step 1 (default): list the exact rows to delete per table. Step 2 (--delete): delete them by exact key in FK-safe order, in ONE transaction, then compare fingerprints."""
import json, sys
import devdb

st = json.load(open('session.json'))
sid = st['schoolId']
q = devdb.psql

# the tables whose row count differs from the "before" fingerprint
before = dict(l.split('=') for l in open('fp_before.txt').read().split())
now = dict(l.split('=') for l in devdb.fingerprint().split())
diff = {t: int(now[t]) - int(before.get(t, 0)) for t in now if int(now[t]) != int(before.get(t, 0))}

user_ids = [int(x) for x in q("select user_id from tb_users order by 1").split()]
assert all(q(f"select org_user_id from tb_users where user_id={u}").strip().startswith(('SUAD', 'ZZDE')) for u in user_ids), 'a non-ZZ user exists: stop'
uid_list = ','.join(map(str, user_ids))

def has_col(t, c):
    return q(f"select 1 from information_schema.columns where table_name='{t}' and column_name='{c}'").strip() == '1'

plan = {}   # table -> (where clause, expected count)
for t, extra in diff.items():
    if t == 'tb_users':
        where = f"user_id in ({uid_list})"
    elif t in ('tb_user_roles', 'tb_refresh_tokens'):
        where = f"user_id in ({uid_list})"
    elif t == 'tb_student_guardians':
        where = f"student_id in (select student_id from public.tb_students where school_id = '{sid}')"
    elif t == 'tb_subscription_pricing':
        where = f"pricing_id = {st['pricingId']}"
    elif has_col(t, 'school_id'):
        where = f"school_id = '{sid}'"
    else:
        raise SystemExit(f'do not know how to select the rows of {t}')
    n = int(q(f"select count(*) from public.{t} where {where}"))
    assert n == extra, f'{t}: would delete {n} but the fingerprint differs by {extra}: stop'
    plan[t] = (where, n)

# FK-safe order: children before parents (Kahn on the foreign keys among these tables)
edges = q("select conrelid::regclass::text || '>' || confrelid::regclass::text from pg_constraint where contype='f'").split()
deps = {t: set() for t in plan}   # table -> tables that reference it
for e in edges:
    child, parent = (x.replace('public.', '') for x in e.split('>'))
    if child in plan and parent in plan and child != parent:
        deps[parent].add(child)
order, remaining = [], set(plan)
while remaining:
    ready = [t for t in sorted(remaining) if not (deps[t] & remaining)]
    if not ready:
        raise SystemExit('cycle in foreign keys: ' + ','.join(sorted(remaining)))
    for t in ready:
        order.append(t)
        remaining.discard(t)

print(f'school {sid}; users {user_ids}; pricing row {st["pricingId"]}')
print('rows to delete (in this order):')
for t in order:
    print(f'  {t:34} {plan[t][1]:>4}   where {plan[t][0]}')
print('total rows:', sum(p[1] for p in plan.values()))

if '--delete' in sys.argv:
    sql = 'begin;\n' + '\n'.join(f'delete from public.{t} where {plan[t][0]};' for t in order) + '\ncommit;'
    print(q(sql, tuples=False).strip().replace('\n', ' | '))
    after = devdb.fingerprint()
    open('fp_final.txt', 'w').write(after)
    a = dict(l.split('=') for l in after.split())
    bad = {t: (before.get(t), a.get(t)) for t in set(before) | set(a) if before.get(t) != a.get(t)}
    print('fingerprint (', len(a), 'tables ) differences from the before-fingerprint:', bad or 'NONE')
