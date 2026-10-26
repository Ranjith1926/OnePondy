import { render } from '@testing-library/react-native';
import type { ReactElement, ReactNode } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { ThemeProvider, type ColorScheme } from '../design-system';
import { AppStateProvider, initialAppState, type AppState } from '../state';

interface Options {
  scheme?: ColorScheme;
  initialState?: AppState;
}

/** Renders UI with the providers components expect: safe area, theme and app state. */
export function renderWithProviders(
  ui: ReactElement,
  { scheme = 'light', initialState = initialAppState }: Options = {},
) {
  function TestProviders({ children }: { children: ReactNode }) {
    return (
      <SafeAreaProvider>
        <AppStateProvider initialState={initialState}>
          <ThemeProvider scheme={scheme}>{children}</ThemeProvider>
        </AppStateProvider>
      </SafeAreaProvider>
    );
  }
  return render(ui, { wrapper: TestProviders });
}
