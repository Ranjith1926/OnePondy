import { createContext, useContext, useMemo, type ReactNode } from 'react';

import type { ColorScheme } from '../tokens/colors';
import { themes, type Theme } from './theme';

const ThemeContext = createContext<Theme>(themes.light);

interface ThemeProviderProps {
  scheme: ColorScheme;
  children: ReactNode;
}

export function ThemeProvider({ scheme, children }: ThemeProviderProps) {
  return <ThemeContext value={themes[scheme]}>{children}</ThemeContext>;
}

export function useTheme(): Theme {
  return useContext(ThemeContext);
}

/**
 * Returns styles built from the current theme. `factory` should return `StyleSheet.create(...)`
 * and be defined at module scope so it stays stable between renders.
 */
export function useThemedStyles<T>(factory: (theme: Theme) => T): T {
  const theme = useTheme();
  return useMemo(() => factory(theme), [factory, theme]);
}
