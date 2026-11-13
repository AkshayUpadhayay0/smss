# Transport live-location check (smss-db-dev ONLY)

Scripted live check for Stage 2 (driver duty and live location). Nothing here may ever point at `smss-db`: `devdb.py` refuses any database except `smss-db-dev`.

1. `dotnet publish smss-api/smss-api -c Release -o <this folder>/pubapi`
2. `python startapi.py TransportLive__LiveSeconds=6 TransportLive__StaleSeconds=14 TransportLive__MinIntervalSeconds=3 TransportLive__ResumeWithinSeconds=20 TransportLive__HideOfflineAfterMinutes=1 RateLimiting__DriverLocation__PermitLimit=400`
   (scratch API on :5292, throwaway JWT key, email pointed at a closed port, scratch upload folders; the DB password is read from user-secrets and never printed)
3. `python stage_a.py` builds the throwaway ZZ school, logins, areas, vehicles and drivers through the API and records every id in `session.json`.
4. `python live_check.py` runs the checks (81 of them), including real-database checks: concurrent starts, the upsert guard, CHECK constraints, the unique open-session index.
5. Clean up with exact ids (from `session.json`) and compare row-count fingerprints before and after. The ZZ accounts and their passwords are throwaway.
