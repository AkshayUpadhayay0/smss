# Driver app: real phone test checklist (Android)

Use a **throwaway driver login in the dev database** (never a real driver, never `smss-db`). The checklist gives two ways to run the app on your phone. Pick A first.

## What you need
- An Android phone, Android 8 or newer (Android 11+ is the interesting case: "Allow all the time" is a Settings step).
- A Windows PC with this repo, Android SDK platform-tools (`adb`), JDK 17, Node.
- The scratch API running against `smss-db-dev` (ask Claude to start it: it uses a throwaway JWT key and mail pointed at a closed port).
- A throwaway driver login in the dev database (the setup script prints each login's id and a random password; Claude gives you the ones to use). The demo driver `ZZDE001DR001` is assigned to vehicle "ZZ Bus 1" and two areas. Never write these passwords into a file in the repo.
- The web Live map open on the PC (`http://localhost:4300`, School Admin login `ZZDE261113001`, password from the setup output) to watch the marker.

## Option A: at your desk, over a USB cable (no internet exposure at all)
1. Phone: Settings > About phone > tap "Build number" 7 times; then Settings > Developer options > turn on **USB debugging**. Plug the phone into the PC and accept the prompt.
2. PC: `adb devices` must list your phone as `device`.
3. Make the PC's API reachable as `localhost` on the phone: `adb reverse tcp:5292 tcp:5292` (and `adb reverse tcp:8081 tcp:8081` for Metro).
4. In `smss-mobile/.env`: `EXPO_PUBLIC_API_BASE_URL=http://localhost:5292` and, for a quick pass, `EXPO_PUBLIC_DUTY_INTERVAL_SECONDS=15`. Do NOT set `EXPO_PUBLIC_DUTY_ACCURACY` on a real phone (it is only for the emulator). A debug build allows plain http; a release build would not.
5. `cd smss-mobile` then `npx expo run:android` (the first build takes several minutes). It installs the dev build on the phone and starts Metro. Later runs: `npx expo start --dev-client`.
6. Open the app, sign in as the driver, and follow the "What to try" list below.
Limits: the cable keeps the phone next to the PC, so this does not test a real trip, a locked screen for a long time, or your mobile network.

## Option B: a real trip (HTTPS tunnel, short-lived)
The URL is compiled into the app, and a free tunnel gets a new URL every run, so build **after** the tunnel is up and stop the tunnel when you are done.
1. Start the scratch API (Claude does this) and a tunnel to it: `cloudflared tunnel --url http://localhost:5292 --no-autoupdate`. Copy the printed `https://....trycloudflare.com` address.
2. Put it in `smss-mobile/.env` as `EXPO_PUBLIC_API_BASE_URL=https://...` (remove `EXPO_PUBLIC_DUTY_INTERVAL_SECONDS` so the real 60 s interval is used).
3. Build a standalone APK that does not need Metro: `cd smss-mobile`, `npx expo prebuild --platform android`, then `cd android` and `gradlew assembleRelease`
   (the APK is `android/app/build/outputs/apk/release/app-release.apk`; it is signed with the debug key, fine for a test). Or use EAS: set the URL in the EAS `preview` environment and run
   `eas build --profile preview --platform android`.
4. Send the APK to the phone (USB copy or `adb install -r app-release.apk`). Allow "install unknown apps" if asked.
5. Carry the phone on a short trip with the screen locked. Watch the marker on the PC's Live map.
6. **When finished:** stop the tunnel (Ctrl+C), delete the APK and `.env`, and tell Claude to clean up the throwaway data. Anyone with the tunnel URL can reach the scratch API while it runs: keep the test short and use only the throwaway logins.

## What to try (both options)
1. First launch: the **disclosure** appears before any Android permission prompt. Tap Continue.
2. Permissions, one at a time: "While using the app" + Precise; then Settings > Location > **Allow all the time** (Android 11+ opens Settings by itself); then Notifications (Android 13+).
   Try **denying** one once to see the "ask again" note, and (on a spare run) deny twice to see the "Open settings" path.
3. Battery screen: tap "Open battery settings" and set SMSS to **Unrestricted** / "No restrictions" (see the brand hints below).
4. Home: vehicle "ZZ Bus 1", areas, "Off duty". Tap **Mark today's attendance**. A notification "SMSS duty is on" must appear. The Live map should show the vehicle within a minute.
5. Switch to another app, then lock the screen for 5 minutes. The marker should keep moving (Live) on the web map.
6. Swipe SMSS away from the recent apps. Note exactly what happens on the web map (does it stay Live, go Stale, go Offline?) and whether the notification stays.
7. Turn on airplane mode for 2 minutes, then off. The marker should recover by itself without you touching the app.
8. Open the app: the Home card should show "On duty since ..." and a recent "Last sent". If sharing had stopped it shows "Location sharing stopped. Resume?": tap Resume.
9. Have the School Admin deactivate the driver on the web while on duty: within a minute the phone should show "Duty ended" with the reason and the notification should disappear.
10. Tap **End duty** and confirm: the marker disappears from the Live map and the notification goes away.
11. Sign out while on duty: it must warn "This will end your duty".

## Battery settings by brand (where the switches usually are)
- **Samsung:** Settings > Battery > Background usage limits: remove SMSS from "Sleeping apps" / "Deep sleeping apps"; SMSS battery page > Unrestricted.
- **Xiaomi / Redmi / Poco:** Settings > Apps > Manage apps > SMSS > Battery saver > No restrictions; switch on Autostart.
- **Oppo / Realme / OnePlus:** Settings > Battery > SMSS: allow background activity, switch off "Optimise battery use"; allow auto-launch.
- **Vivo / iQOO:** Settings > Battery > Background power consumption > SMSS > allow; allow Autostart.
- **Pixel and most others:** Settings > Apps > SMSS > App battery usage > Unrestricted.
See also https://dontkillmyapp.com for your model.

## What to report back
- Phone brand, model and Android version; which option (A or B).
- For each step above: worked / did not, and anything odd (wording, a button that did nothing, a crash).
- Step 6 (swipe-away): what the Live map showed and when, and whether the notification stayed.
- Step 5: did the marker keep updating with the screen locked? For how long?
- Battery: which setting you changed, and whether the vehicle ever went Stale/Offline by itself during a trip.
- Time of the test (so the API log can be matched), and a screenshot of the Home screen if something looked wrong. Do **not** send the tracking token, passwords or the tunnel URL.
