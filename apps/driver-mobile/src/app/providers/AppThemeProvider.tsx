import { StatusBar } from 'expo-status-bar';
import type { ReactNode } from 'react';
import { useColorScheme } from 'react-native';

import { ThemeProvider } from '../../design-system';

/** Follows the device's light or dark setting. */
export function AppThemeProvider({ children }: { children: ReactNode }) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light';

  return (
    <ThemeProvider scheme={scheme}>
      <StatusBar style={scheme === 'dark' ? 'light' : 'dark'} />
      {children}
    </ThemeProvider>
  );
}
