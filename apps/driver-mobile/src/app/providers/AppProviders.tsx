import type { ReactNode } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { SessionProvider } from '../../session/SessionProvider';
import { AppThemeProvider } from './AppThemeProvider';
import { QueryProvider } from './QueryProvider';

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <SafeAreaProvider>
      <QueryProvider>
        <AppThemeProvider>
          <SessionProvider>{children}</SessionProvider>
        </AppThemeProvider>
      </QueryProvider>
    </SafeAreaProvider>
  );
}
