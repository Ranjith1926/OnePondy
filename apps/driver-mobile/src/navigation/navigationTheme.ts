import { DarkTheme, DefaultTheme, type Theme as NavigationTheme } from '@react-navigation/native';

import { fontFamily, type Theme } from '../design-system';

/** Maps design tokens onto React Navigation's theme (headers, tab bar, screen background). */
export function createNavigationTheme(theme: Theme): NavigationTheme {
  const base = theme.scheme === 'dark' ? DarkTheme : DefaultTheme;
  return {
    ...base,
    dark: theme.scheme === 'dark',
    colors: {
      ...base.colors,
      primary: theme.colors.brand.primary,
      background: theme.colors.background,
      card: theme.colors.surface,
      text: theme.colors.text.primary,
      border: theme.colors.border,
      notification: theme.colors.brand.secondary,
    },
    fonts: {
      regular: { fontFamily, fontWeight: '400' },
      medium: { fontFamily, fontWeight: '500' },
      bold: { fontFamily, fontWeight: '700' },
      heavy: { fontFamily, fontWeight: '800' },
    },
  };
}
