import type { ReactNode } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { AppStateProvider, type AppState } from '../../state';
import { AppThemeProvider } from './AppThemeProvider';
import { LanguageSync } from './LanguageSync';
import { PreferencesBootstrap } from './PreferencesBootstrap';

interface AppProvidersProps {
  children: ReactNode;
  /** For tests: start from a specific app state. */
  initialState?: AppState;
}

// The query client (TanStack Query) and session bootstrap join in Phase 2.
export function AppProviders({ children, initialState }: AppProvidersProps) {
  return (
    <SafeAreaProvider>
      <AppStateProvider initialState={initialState}>
        <PreferencesBootstrap>
          <LanguageSync />
          <AppThemeProvider>{children}</AppThemeProvider>
        </PreferencesBootstrap>
      </AppStateProvider>
    </SafeAreaProvider>
  );
}
