export type ColorScheme = 'light' | 'dark';

export interface ColorTokens {
  brand: {
    /** Deep teal: primary buttons, active navigation, links. */
    primary: string;
    primaryPressed: string;
    onPrimary: string;
    primaryContainer: string;
    onPrimaryContainer: string;
    /** Warm amber accent, used sparingly. */
    secondary: string;
    onSecondary: string;
    secondaryContainer: string;
    onSecondaryContainer: string;
  };
  /** App background: warm sand in light mode. */
  background: string;
  /** Cards, sheets and the tab bar. */
  surface: string;
  /** Inputs, chips and quiet fills. */
  surfaceMuted: string;
  text: {
    primary: string;
    secondary: string;
    /** Disabled text only; exempt from contrast minimums. */
    disabled: string;
    link: string;
  };
  /** Dividers and card outlines. */
  border: string;
  /** Input and control outlines; meets 3:1 non-text contrast. */
  borderStrong: string;
  success: string;
  successContainer: string;
  onSuccessContainer: string;
  warning: string;
  warningContainer: string;
  onWarningContainer: string;
  error: string;
  errorContainer: string;
  onErrorContainer: string;
  info: string;
  infoContainer: string;
  onInfoContainer: string;
  scrim: string;
  skeleton: string;
}

const light: ColorTokens = {
  brand: {
    primary: '#0E6F6A',
    primaryPressed: '#0A5853',
    onPrimary: '#FFFFFF',
    primaryContainer: '#D5EEEB',
    onPrimaryContainer: '#08433F',
    secondary: '#E8A33D',
    onSecondary: '#2A1B03',
    secondaryContainer: '#FCEBD0',
    onSecondaryContainer: '#5C3A06',
  },
  background: '#FAF7F2',
  surface: '#FFFFFF',
  surfaceMuted: '#F2EDE5',
  text: {
    primary: '#1C2421',
    secondary: '#55615C',
    disabled: '#9AA39F',
    link: '#0E6F6A',
  },
  border: '#E6E0D6',
  borderStrong: '#8C8578',
  success: '#1D7A47',
  successContainer: '#DDF3E5',
  onSuccessContainer: '#0F4D2B',
  warning: '#9A5B00',
  warningContainer: '#FFEFD2',
  onWarningContainer: '#5E3700',
  error: '#B3261E',
  errorContainer: '#FBE1DE',
  onErrorContainer: '#7A1712',
  info: '#1F5FAD',
  infoContainer: '#DDEAFB',
  onInfoContainer: '#123E73',
  scrim: 'rgba(20, 26, 24, 0.48)',
  skeleton: '#ECE6DC',
};

const dark: ColorTokens = {
  brand: {
    primary: '#5CC3B9',
    primaryPressed: '#46A99F',
    onPrimary: '#052A27',
    primaryContainer: '#14403C',
    onPrimaryContainer: '#BFEDE7',
    secondary: '#F2B55C',
    onSecondary: '#2A1B03',
    secondaryContainer: '#4A3110',
    onSecondaryContainer: '#FCE3BC',
  },
  background: '#0E1413',
  surface: '#161F1D',
  surfaceMuted: '#1F2A28',
  text: {
    primary: '#EDF2F0',
    secondary: '#A7B3AF',
    disabled: '#66736F',
    link: '#5CC3B9',
  },
  border: '#27332F',
  borderStrong: '#73827D',
  success: '#6CCB95',
  successContainer: '#143823',
  onSuccessContainer: '#C4F0D5',
  warning: '#F2B655',
  warningContainer: '#3D2A0B',
  onWarningContainer: '#FBE2B6',
  error: '#F2877D',
  errorContainer: '#45160F',
  onErrorContainer: '#FCD6D1',
  info: '#8AB8F0',
  infoContainer: '#13304F',
  onInfoContainer: '#D3E5FB',
  scrim: 'rgba(0, 0, 0, 0.6)',
  skeleton: '#24302D',
};

export const colorTokens: Record<ColorScheme, ColorTokens> = { light, dark };
