/** Words the driver sees. The disclosure text is fixed by the product owner: change it only with their approval (it is also what the Play Console review sees). */
export const DISCLOSURE_TEXT =
  "SMSS shares this vehicle's location with your school, and with students assigned to your area, only while you are on duty. Sharing starts when you tap Mark today's attendance and stops when you tap End duty. SMSS does not keep a location history.";

/** Bump when DISCLOSURE_TEXT changes: drivers then see it again. */
export const DISCLOSURE_VERSION = 1;

export const NOTIFICATION_TITLE = 'SMSS duty is on';
export const NOTIFICATION_BODY = "SMSS is sharing this vehicle's location while you are on duty";

export const SIGN_OUT_ON_DUTY_WARNING = 'This will end your duty. Your school will stop seeing this vehicle, and you will need to sign in again.';

/** Manufacturer hints for the battery screen. Menu names differ by Android version; these are the usual places. */
export const BATTERY_HINTS: { maker: string; hint: string }[] = [
  { maker: 'Samsung', hint: 'Settings > Battery > Background usage limits: remove SMSS from "Sleeping apps" and "Deep sleeping apps", and set SMSS to "Unrestricted" in its battery page.' },
  { maker: 'Xiaomi / Redmi / Poco', hint: 'Settings > Apps > Manage apps > SMSS > Battery saver: choose "No restrictions". Also switch on "Autostart" for SMSS.' },
  { maker: 'Oppo / Realme / OnePlus', hint: 'Settings > Battery > SMSS (or App battery management): allow background activity and switch off "Optimise battery use". Switch on "Allow auto-launch" if you see it.' },
  { maker: 'Vivo / iQOO', hint: 'Settings > Battery > Background power consumption > SMSS: allow high background power use. Also allow "Autostart".' },
  { maker: 'Google Pixel and most other phones', hint: 'Settings > Apps > SMSS > App battery usage: choose "Unrestricted".' },
];

export const STOP_MESSAGES = {
  UNKNOWN_TOKEN: 'Your duty session is no longer valid, so location sharing stopped.',
  SESSION_ENDED: 'Your duty has ended, so location sharing stopped.',
  SESSION_EXPIRED: 'Your duty session reached its time limit, so location sharing stopped. Start duty again to continue.',
  DRIVER_DEACTIVATED: 'Your driver account is inactive, so duty has ended. Please contact your school.',
  NO_TOKEN: 'Location sharing was no longer authorised on this phone, so it stopped.',
} as const;
