import { StatusBar } from 'expo-status-bar';
import type { ReactNode } from 'react';
import { useColorScheme } from 'react-native';

import { ThemeProvider, type ColorScheme } from '../../design-system';
import { useAppState } from '../../state';

/** Applies the user's appearance preference, following the device when set to "system". */
export function AppThemeProvider({ children }: { children: ReactNode }) {
  const { preferences } = useAppState();
  const deviceScheme = useColorScheme();
  const scheme: ColorScheme =
    preferences.theme === 'system'
      ? deviceScheme === 'dark'
        ? 'dark'
        : 'light'
      : preferences.theme;

  return (
    <ThemeProvider scheme={scheme}>
      <StatusBar style={scheme === 'dark' ? 'light' : 'dark'} />
      {children}
    </ThemeProvider>
  );
}
