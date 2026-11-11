import { Inter_400Regular, Inter_500Medium, Inter_600SemiBold, Inter_700Bold, useFonts } from '@expo-google-fonts/inter';
import { focusManager, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useState } from 'react';
import { AppState, Platform } from 'react-native';
import { Text } from '../src/components/Text';
import { Screen } from '../src/components/Screen';
import { SessionProvider, useSession } from '../src/auth/session';
import { env } from '../src/config/env';
import { ApiError } from '../src/api/errors';

SplashScreen.preventAutoHideAsync().catch(() => {});

// React Query's focus = the app coming back to the foreground, so a student who opens the app the next morning sees fresh data (and the new server date).
function useAppFocus() {
  useEffect(() => {
    if (Platform.OS === 'web') return;
    const sub = AppState.addEventListener('change', (state) => focusManager.setFocused(state === 'active'));
    return () => sub.remove();
  }, []);
}

function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        // Online-only app: retry a flaky network a couple of times, but never retry a definite answer (4xx).
        retry: (count, error) => !(error instanceof ApiError && error.kind !== 'network' && error.kind !== 'server') && count < 2,
      },
    },
  });
}

function RootStack() {
  const { status, role, needsPasswordChange } = useSession();
  const signedOut = status === 'signedOut';
  // Fail closed: a route group is reachable only while its guard is true. A first-login account reaches (auth)/change-password and nothing else.
  const inApp = status === 'signedIn' && !needsPasswordChange;
  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="index" />
      <Stack.Protected guard={signedOut || needsPasswordChange}>
        <Stack.Screen name="(auth)" />
      </Stack.Protected>
      <Stack.Protected guard={inApp && role === 'STUDENT'}>
        <Stack.Screen name="(student)" />
      </Stack.Protected>
      <Stack.Protected guard={inApp && role === 'TEACHER'}>
        <Stack.Screen name="(teacher)" />
      </Stack.Protected>
      <Stack.Protected guard={inApp && role === 'UNSUPPORTED'}>
        <Stack.Screen name="unsupported" />
      </Stack.Protected>
    </Stack>
  );
}

/** Keeps the splash screen up until fonts are loaded and the stored session has been checked. */
function Gate() {
  const { status } = useSession();
  const [fontsLoaded, fontError] = useFonts({ Inter_400Regular, Inter_500Medium, Inter_600SemiBold, Inter_700Bold });
  const ready = (fontsLoaded || !!fontError) && status !== 'loading';

  useEffect(() => {
    if (ready) SplashScreen.hideAsync().catch(() => {});
  }, [ready]);

  if (!ready) return null;
  return <RootStack />;
}

export default function RootLayout() {
  const [queryClient] = useState(makeQueryClient);
  useAppFocus();

  // A missing / non-HTTPS API URL is a build mistake: say so plainly instead of failing on the first request.
  let configError: string | null = null;
  try {
    void env.apiBaseUrl;
  } catch (e) {
    configError = e instanceof Error ? e.message : 'The app is not configured.';
  }
  useEffect(() => {
    if (configError) SplashScreen.hideAsync().catch(() => {});
  }, [configError]);
  if (configError) {
    return (
      <Screen title="Configuration error">
        <Text>{configError}</Text>
      </Screen>
    );
  }

  return (
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <StatusBar style="dark" />
        <Gate />
      </SessionProvider>
    </QueryClientProvider>
  );
}
