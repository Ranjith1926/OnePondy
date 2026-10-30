import { render } from '@testing-library/react-native';
import type { ReactElement, ReactNode } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { ThemeProvider, type ColorScheme } from '../design-system';

interface Options {
  scheme?: ColorScheme;
}

/** Renders UI with the providers design-system components expect: safe area and theme. */
export function renderWithProviders(ui: ReactElement, { scheme = 'light' }: Options = {}) {
  function TestProviders({ children }: { children: ReactNode }) {
    return (
      <SafeAreaProvider>
        <ThemeProvider scheme={scheme}>{children}</ThemeProvider>
      </SafeAreaProvider>
    );
  }
  return render(ui, { wrapper: TestProviders });
}
