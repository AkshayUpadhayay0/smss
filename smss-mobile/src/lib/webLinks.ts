/**
 * The website address the parent pages live on, for "Set up your account" and "Forgot ID or password". The app has no separate setting for it (and the app configuration is not touched
 * for this): it is the origin of the Privacy Policy address that is already configured (EXPO_PUBLIC_PRIVACY_POLICY_URL, https only). Null when that is not set: the links are then hidden.
 */
export function webPageUrl(privacyPolicyUrl: string | null | undefined, path: string): string | null {
  if (!privacyPolicyUrl) return null;
  try {
    const origin = new URL(privacyPolicyUrl);
    if (origin.protocol !== 'https:') return null;
    return origin.origin + (path.startsWith('/') ? path : `/${path}`);
  } catch {
    return null;
  }
}

/** The one-time set-password / reset pages. They are opened from the WhatsApp or email message; the app only links to the page that asks for the login id. */
export const FORGOT_PATH = '/parent/forgot';
