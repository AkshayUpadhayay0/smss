# Transport live-location check (smss-db-dev ONLY)

Scripted live check for Stage 2 (driver duty and live location). Nothing here may ever point at `smss-db`: `devdb.py` refuses any database except `smss-db-dev`.

1. `dotnet publish smss-api/smss-api -c Release -o <this folder>/pubapi`
2. `python startapi.py TransportLive__LiveSeconds=6 TransportLive__StaleSeconds=14 TransportLive__MinIntervalSeconds=3 TransportLive__ResumeWithinSeconds=20 TransportLive__HideOfflineAfterMinutes=1 RateLimiting__DriverLocation__PermitLimit=400`
   (scratch API on :5292, throwaway JWT key, email pointed at a closed port, scratch upload folders; the DB password is read from user-secrets and never printed)
3. `python stage_a.py` builds the throwaway ZZ school, logins, areas, vehicles and drivers through the API and records every id and each login's **random** password in `session.json` (git-ignored; it is printed once to your terminal).
4. `python live_check.py` runs the checks (81 of them), including real-database checks: concurrent starts, the upsert guard, CHECK constraints, the unique open-session index.
5. Clean up with exact ids (from `session.json`) and compare row-count fingerprints before and after. The ZZ accounts and their passwords are throwaway.

Other scripts: `sim_driver.py D1|D2 [seconds]` drives a looped route like the driver app (needs the scratch API and the logins from `stage_a.py`); `rolecheck_setup.py` adds a throwaway Teacher and Student for the mobile Student / Teacher regression check; `cleanup.py` lists, then (with `--delete`) deletes by exact key in FK-safe order, every ZZ row in `smss-db-dev` and compares the row-count fingerprint with `fp_before.txt` (take that file with `python devdb.py fp > fp_before.txt` BEFORE `stage_a.py`).

## Stage D (approximate pickup and drop times)
Run `Docs/smss-transport-times.sql` on `smss-db-dev` first, then: `stage_a.py`, a throwaway student (`rolecheck_setup.py`, or `stage_d_setup.py` when the dev DB predates the global admission-type migration: it seeds one ZZ student with a single SQL row and creates the login through the API), `live_check_d.py` (55 checks: create / update / clear, invalid input, the CHECK constraint, roles, the driver app, the student view with two drivers on one area and exactly one list per driver) and `live_check_d_isolation.py` (registers a second ZZ school, `schoolB` in `session.json`, and proves nothing crosses between the schools). `stage_a.py` now sends `areas: [{areaId, morningPickupTime, afternoonDropTime}]` (the old `areaIds` is refused with a 400).
