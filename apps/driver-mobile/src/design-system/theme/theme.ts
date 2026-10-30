import { colorTokens, type ColorScheme, type ColorTokens } from '../tokens/colors';
import {
  elevationTokens,
  layout,
  motion,
  radii,
  spacing,
  type ElevationTokens,
} from '../tokens/layout';
import { typography } from '../tokens/typography';

export interface Theme {
  scheme: ColorScheme;
  colors: ColorTokens;
  elevation: ElevationTokens;
  typography: typeof typography;
  spacing: typeof spacing;
  radii: typeof radii;
  layout: typeof layout;
  motion: typeof motion;
}

export function createTheme(scheme: ColorScheme): Theme {
  return {
    scheme,
    colors: colorTokens[scheme],
    elevation: elevationTokens[scheme],
    typography,
    spacing,
    radii,
    layout,
    motion,
  };
}

export const themes: Record<ColorScheme, Theme> = {
  light: createTheme('light'),
  dark: createTheme('dark'),
};
