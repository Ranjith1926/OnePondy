import type { ColorScheme } from './colors';

export const spacing = {
  none: 0,
  xxs: 4,
  xs: 8,
  sm: 12,
  md: 16,
  lg: 20,
  xl: 24,
  xxl: 32,
} as const;

export type SpacingToken = keyof typeof spacing;

export const radii = {
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  pill: 999,
} as const;

export type RadiusToken = keyof typeof radii;

export const layout = {
  /** Minimum touch target in dp (Android accessibility guideline). */
  minTouchTarget: 48,
  /** Horizontal padding for screen content. */
  screenPadding: spacing.lg,
} as const;

export interface ElevationTokens {
  card: string | undefined;
  raised: string | undefined;
}

// Restrained shadows in light mode; dark mode relies on surface contrast and borders instead.
export const elevationTokens: Record<ColorScheme, ElevationTokens> = {
  light: {
    card: '0px 1px 2px rgba(28, 36, 33, 0.06), 0px 4px 12px rgba(28, 36, 33, 0.06)',
    raised: '0px 8px 24px rgba(28, 36, 33, 0.16)',
  },
  dark: {
    card: undefined,
    raised: '0px 8px 24px rgba(0, 0, 0, 0.5)',
  },
};

export const motion = {
  duration: {
    fast: 120,
    standard: 220,
    slow: 320,
  },
  /** Opacity applied to pressed controls that have no ripple. */
  pressedOpacity: 0.85,
} as const;
