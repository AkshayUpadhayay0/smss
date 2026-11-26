import { Redirect, type Href } from 'expo-router';
import { useSession } from '../src/auth/session';

/**
 * The anchor route. Whenever a guard in the root layout closes (sign-out, first-login lock, ...), the router lands here and this
 * sends the user to the one place their state allows. All role logic comes from useSession (which uses src/auth/roles.ts).
 */
export default function Index() {
  const { status, role, needsPasswordChange } = useSession();
  if (status === 'loading') return null;
  if (status === 'signedOut') return <Redirect href="/sign-in" />;
  if (needsPasswordChange) return <Redirect href="/change-password" />;
  if (role === 'STUDENT') return <Redirect href="/(student)/(tabs)" />;
  if (role === 'TEACHER') return <Redirect href="/(teacher)/(tabs)" />;
  if (role === 'DRIVER') return <Redirect href="/(driver)" />;
  if (role === 'SCHOOL_ADMIN') return <Redirect href={'/(admin)' as Href} />;
  // a parent always lands on the family screen, even with one child
  if (role === 'GUARDIAN') return <Redirect href={'/(family)/(tabs)' as Href} />;
  return <Redirect href="/unsupported" />;
}
